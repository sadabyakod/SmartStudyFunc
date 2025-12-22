using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services;
using SmartStudyFunc.Services.Evaluation;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// Queue-triggered function for processing written answer submissions.
    /// 
    /// ARCHITECTURE:
    /// - Triggered by: written-submission-processing queue
    /// - Performs: OCR extraction + AI evaluation in single execution
    /// - Idempotent: Skips if status is Completed or Evaluating
    /// - Fault-tolerant: Retries with exponential backoff, max 3 attempts
    /// 
    /// STATUS FLOW:
    /// Uploaded → OcrProcessing → Evaluating → Completed
    ///                ↓                ↓
    ///              Failed          Failed
    /// 
    /// LOG STAGES:
    /// [QUEUE_RECEIVED] → [OCR_STARTED] → [OCR_COMPLETED] → 
    /// [EVALUATION_STARTED] → [EVALUATION_COMPLETED] | [RETRY_SCHEDULED] | [FAILED_FINAL]
    /// </summary>
    public class ProcessWrittenSubmission
    {
        private readonly IGoogleVisionOcrService _ocrService;
        private readonly IDualOcrService? _dualOcrService;
        private readonly IWrittenAnswerEvaluationService _evaluationService;
        private readonly ISubjectRouter _subjectRouter;
        private readonly IWrittenSubmissionRepository _repository;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProcessWrittenSubmission> _logger;

        private const int MaxRetries = 3;
        private const string QueueName = "written-submission-processing";
        private const int MaxTextLengthForSql = 100000; // 100KB threshold

        public ProcessWrittenSubmission(
            IGoogleVisionOcrService ocrService,
            IWrittenAnswerEvaluationService evaluationService,
            ISubjectRouter subjectRouter,
            IWrittenSubmissionRepository repository,
            BlobServiceClient blobServiceClient,
            QueueServiceClient queueServiceClient,
            IConfiguration configuration,
            ILogger<ProcessWrittenSubmission> logger,
            IDualOcrService? dualOcrService = null)
        {
            _ocrService = ocrService;
            _dualOcrService = dualOcrService;
            _evaluationService = evaluationService;
            _subjectRouter = subjectRouter;
            _repository = repository;
            _blobServiceClient = blobServiceClient;
            _queueServiceClient = queueServiceClient;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Main entry point for written submission processing.
        /// Handles both OCR and AI evaluation in a single execution.
        /// </summary>
        [Function(nameof(ProcessWrittenSubmission))]
        public async Task Run(
            [QueueTrigger(QueueName)] string messageText,
            FunctionContext context,
            CancellationToken cancellationToken)
        {
            var totalStopwatch = Stopwatch.StartNew();
            WrittenSubmissionProcessingMessage? message = null;
            Guid submissionId = Guid.Empty;
            string examId = string.Empty;
            string studentId = string.Empty;

            try
            {
                // ════════════════════════════════════════════════════════════════
                // PHASE 1: Parse and validate queue message
                // ════════════════════════════════════════════════════════════════
                message = JsonSerializer.Deserialize<WrittenSubmissionProcessingMessage>(
                    messageText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (message == null || message.WrittenSubmissionId == Guid.Empty)
                {
                    _logger.LogError(
                        "[QUEUE_RECEIVED] Invalid message. Could not deserialize: {Message}",
                        messageText.Substring(0, Math.Min(500, messageText.Length)));
                    return; // Don't retry invalid messages
                }

                submissionId = message.WrittenSubmissionId;
                examId = message.ExamId;
                studentId = message.StudentId;

                _logger.LogInformation(
                    "[QUEUE_RECEIVED] SubmissionId={SubmissionId}, ExamId={ExamId}, StudentId={StudentId}, Files={FileCount}, RetryCount={RetryCount}",
                    submissionId, examId, studentId, message.FilePaths.Count, message.RetryCount);

                // ════════════════════════════════════════════════════════════════
                // PHASE 2: Load submission and perform idempotency check
                // ════════════════════════════════════════════════════════════════
                var submission = await _repository.GetByIdAsync(submissionId, cancellationToken);

                if (submission == null)
                {
                    _logger.LogError(
                        "[FAILED_FINAL] SubmissionId={SubmissionId} not found in database. Aborting.",
                        submissionId);
                    return; // Don't retry - submission doesn't exist
                }

                // IDEMPOTENCY CHECK 1: Already completed
                if (submission.Status == WrittenSubmissionStatus.Completed)
                {
                    _logger.LogInformation(
                        "[QUEUE_RECEIVED] SubmissionId={SubmissionId} already Completed. Skipping duplicate.",
                        submissionId);
                    return;
                }

                // IDEMPOTENCY CHECK 2: Already evaluating (another instance may be processing)
                if (submission.Status == WrittenSubmissionStatus.Evaluating)
                {
                    // Check if evaluation started recently (within 3 minutes) - if so, skip
                    if (submission.EvaluationStartedAt.HasValue && 
                        (DateTime.UtcNow - submission.EvaluationStartedAt.Value).TotalMinutes < 3)
                    {
                        _logger.LogInformation(
                            "[QUEUE_RECEIVED] SubmissionId={SubmissionId} is Evaluating (started {Minutes:F1}m ago). Skipping.",
                            submissionId, (DateTime.UtcNow - submission.EvaluationStartedAt.Value).TotalMinutes);
                        return;
                    }
                    // Otherwise, evaluation may have stalled - allow retry
                    _logger.LogWarning(
                        "[QUEUE_RECEIVED] SubmissionId={SubmissionId} was Evaluating but stalled for {Minutes:F1}m. Reprocessing.",
                        submissionId, (DateTime.UtcNow - submission.EvaluationStartedAt.Value).TotalMinutes);
                }

                // IDEMPOTENCY CHECK 3: Max retries exceeded
                if (message.RetryCount >= MaxRetries)
                {
                    _logger.LogError(
                        "[FAILED_FINAL] SubmissionId={SubmissionId} exceeded MaxRetries={MaxRetries}. Marking failed.",
                        submissionId, MaxRetries);

                    await _repository.UpdateStatusAsync(
                        submissionId,
                        WrittenSubmissionStatus.Failed,
                        $"Processing failed after {MaxRetries} attempts",
                        cancellationToken);
                    return;
                }

                // ════════════════════════════════════════════════════════════════
                // PHASE 3: OCR Processing (Google Cloud Vision)
                // ════════════════════════════════════════════════════════════════
                var ocrStopwatch = Stopwatch.StartNew();

                await _repository.UpdateStatusAsync(
                    submissionId,
                    WrittenSubmissionStatus.OcrProcessing,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "[OCR_STARTED] SubmissionId={SubmissionId}, FileCount={FileCount}",
                    submissionId, message.FilePaths.Count);

                // Check if dual OCR is enabled
                var useDualOcr = _configuration.GetValue<bool>("OCR:DualVerificationEnabled", false) && _dualOcrService != null;
                
                string extractedText;
                float avgConfidence;
                
                if (useDualOcr)
                {
                    _logger.LogInformation("[OCR] Using DUAL OCR (Google + Azure) for SubmissionId={SubmissionId}", submissionId);
                    var dualResult = await _dualOcrService!.ExtractTextFromBlobsAsync(
                        message.FilePaths,
                        submissionId,
                        cancellationToken);
                    
                    if (!dualResult.Success)
                    {
                        _logger.LogError(
                            "[OCR_FAILED] Dual OCR failed. SubmissionId={SubmissionId}, Error={Error}",
                            submissionId, dualResult.ErrorMessage);
                        await HandleRetryOrFailAsync(
                            message,
                            $"Dual OCR failed: {dualResult.ErrorMessage}",
                            cancellationToken);
                        return;
                    }
                    
                    extractedText = dualResult.CombinedText;
                    avgConfidence = Math.Max(dualResult.GoogleConfidence, dualResult.AzureConfidence);
                    
                    _logger.LogInformation(
                        "[OCR_SUCCESS] Dual OCR completed. Primary={Primary}, GoogleConf={GoogleConf:F2}, AzureConf={AzureConf:F2}",
                        dualResult.PrimaryEngine, dualResult.GoogleConfidence, dualResult.AzureConfidence);
                }
                else
                {
                    _logger.LogInformation("[OCR] Using Google Vision OCR for SubmissionId={SubmissionId}", submissionId);
                    var ocrResult = await _ocrService.ExtractTextFromBlobsAsync(
                        message.FilePaths,
                        submissionId,
                        cancellationToken);
                    
                    if (!ocrResult.Success)
                    {
                        _logger.LogError(
                            "[OCR_FAILED] SubmissionId={SubmissionId}, Error={Error}, DurationMs={Duration}",
                            submissionId, ocrResult.ErrorMessage, ocrStopwatch.ElapsedMilliseconds);

                        await HandleRetryOrFailAsync(
                            message,
                            $"OCR failed: {ocrResult.ErrorMessage}",
                            cancellationToken);
                        return;
                    }
                    
                    extractedText = ocrResult.CombinedText;
                    avgConfidence = ocrResult.AverageConfidence;
                }

                ocrStopwatch.Stop();

                var extractedTextJson = JsonSerializer.Serialize(new { text = extractedText });
                string? textBlobPath = null;

                // Store large text in blob storage
                if (extractedText.Length > MaxTextLengthForSql)
                {
                    textBlobPath = await SaveTextToBlobAsync(
                        submissionId, examId, extractedText, cancellationToken);
                    
                    _logger.LogInformation(
                        "[OCR_COMPLETED] SubmissionId={SubmissionId}, Chars={CharCount} (stored in blob), Confidence={Confidence:P2}, DurationMs={Duration}",
                        submissionId, extractedText.Length, avgConfidence, ocrStopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation(
                        "[OCR_COMPLETED] SubmissionId={SubmissionId}, Chars={CharCount}, Confidence={Confidence:P2}, DurationMs={Duration}",
                        submissionId, extractedText.Length, avgConfidence, ocrStopwatch.ElapsedMilliseconds);
                }

                await _repository.SaveExtractedTextAsync(
                    submissionId,
                    extractedText,
                    extractedTextJson,
                    textBlobPath,
                    ocrStopwatch.ElapsedMilliseconds,
                    cancellationToken);

                // ════════════════════════════════════════════════════════════════
                // PHASE 4: AI Answer Evaluation (Azure OpenAI)
                // ════════════════════════════════════════════════════════════════
                var evalStopwatch = Stopwatch.StartNew();

                await _repository.UpdateStatusAsync(
                    submissionId,
                    WrittenSubmissionStatus.Evaluating,
                    cancellationToken: cancellationToken);

                // Fetch exam questions with rubrics
                _logger.LogWarning(
                    "[FETCH-QUESTIONS] SubmissionId={SubmissionId}, ExamId={ExamId} - Fetching questions from database...",
                    submissionId, examId);
                    
                var questions = await _repository.GetExamQuestionsWithRubricsAsync(
                    examId, cancellationToken);

                _logger.LogWarning(
                    "[FETCH-QUESTIONS] SubmissionId={SubmissionId} - Retrieved {QuestionCount} questions",
                    submissionId, questions.Count);

                if (questions.Count == 0)
                {
                    _logger.LogError(
                        "[FAILED_FINAL] SubmissionId={SubmissionId}, ExamId={ExamId} has no questions configured.",
                        submissionId, examId);

                    await _repository.UpdateStatusAsync(
                        submissionId,
                        WrittenSubmissionStatus.Failed,
                        $"No questions configured for exam {examId}",
                        cancellationToken);
                    return;
                }

                _logger.LogInformation(
                    "[EVALUATION_STARTED] SubmissionId={SubmissionId}, QuestionCount={QuestionCount}",
                    submissionId, questions.Count);

                // Check if V2 evaluation is enabled
                var useV2 = _configuration.GetValue<bool>("Evaluation:UseV2", false);
                
                _logger.LogWarning(
                    "[PROCESS-EVAL] === CALLING EVALUATION SERVICE === SubmissionId={SubmissionId}, Questions={QuestionCount}, UseV2={UseV2}",
                    submissionId, questions.Count, useV2);
                
                // Hard timeout for entire evaluation: 2 minutes max
                using var evaluationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                evaluationCts.CancelAfter(TimeSpan.FromMinutes(2));
                
                WrittenEvaluationResult evaluationResult;
                try
                {
                    if (useV2)
                    {
                        // Use V2 evaluation engine
                        evaluationResult = await EvaluateWithV2Async(
                            submission,
                            extractedText,
                            questions,
                            evaluationCts.Token);
                    }
                    else
                    {
                        // Use V1 evaluation service
                        evaluationResult = await _evaluationService.EvaluateSubmissionAsync(
                            submission,
                            extractedText,
                            questions,
                            evaluationCts.Token);
                    }

                    _logger.LogWarning(
                        "[PROCESS-EVAL] === EVALUATION SERVICE RETURNED === SubmissionId={SubmissionId}, TotalScore={Score}, Engine={Engine}",
                        submissionId, evaluationResult.TotalScore, useV2 ? "V2" : "V1");
                }
                catch (OperationCanceledException) when (evaluationCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(
                        "[PROCESS-EVAL] === EVALUATION TIMEOUT === SubmissionId={SubmissionId} - Evaluation exceeded 2 minutes. This is likely due to missing exam questions or database issues.",
                        submissionId);
                    
                    await _repository.UpdateStatusAsync(
                        submissionId,
                        WrittenSubmissionStatus.Failed,
                        "Evaluation timeout after 2 minutes. Check if exam questions exist in database.",
                        cancellationToken);
                    return;
                }

                evalStopwatch.Stop();

                // ════════════════════════════════════════════════════════════════
                // PHASE 4.5: Merge MCQ data from backend API (if present)
                // ════════════════════════════════════════════════════════════════
                if (submission.McqAnswers != null || submission.McqScore.HasValue)
                {
                    _logger.LogInformation(
                        "[MCQ_MERGE] SubmissionId={SubmissionId}, McqScore={McqScore}, McqTotalMarks={McqTotalMarks}",
                        submissionId, submission.McqScore, submission.McqTotalMarks);
                    
                    // Add MCQ data from database to evaluation result
                    MergeMcqDataIntoEvaluationResult(submission, evaluationResult);
                }

                // ════════════════════════════════════════════════════════════════
                // PHASE 5: Save Results to Blob Storage (Required for Completion)
                // ════════════════════════════════════════════════════════════════
                string? resultBlobPath = null;
                int blobRetryCount = 0;
                const int maxBlobRetries = 3;
                
                while (resultBlobPath == null && blobRetryCount < maxBlobRetries)
                {
                    try
                    {
                        resultBlobPath = await SaveEvaluationResultToBlobAsync(
                            submissionId,
                            examId,
                            evaluationResult,
                            cancellationToken);
                        
                        _logger.LogInformation(
                            "[RESULT_SAVED_TO_BLOB] SubmissionId={SubmissionId}, BlobPath={BlobPath}",
                            submissionId, resultBlobPath);
                    }
                    catch (Exception ex)
                    {
                        blobRetryCount++;
                        _logger.LogWarning(ex,
                            "[RESULT_BLOB_SAVE_FAILED] SubmissionId={SubmissionId}, Attempt={Attempt}/{MaxAttempts}",
                            submissionId, blobRetryCount, maxBlobRetries);
                        
                        if (blobRetryCount < maxBlobRetries)
                        {
                            await Task.Delay(1000 * blobRetryCount, cancellationToken); // Exponential backoff
                        }
                    }
                }

                // Only mark as Completed if blob save succeeded
                if (string.IsNullOrEmpty(resultBlobPath))
                {
                    _logger.LogError(
                        "[BLOB_SAVE_FAILED_FINAL] SubmissionId={SubmissionId}. Cannot complete without blob storage.",
                        submissionId);
                    
                    await _repository.UpdateStatusAsync(
                        submissionId,
                        WrittenSubmissionStatus.Failed,
                        "Failed to save evaluation results to blob storage after retries",
                        cancellationToken);
                    return;
                }

                // ════════════════════════════════════════════════════════════════
                // PHASE 6: Save Results to Database and Update Status to Completed
                // ════════════════════════════════════════════════════════════════
                await _repository.SaveEvaluationResultAsync(
                    evaluationResult,
                    resultBlobPath,
                    evalStopwatch.ElapsedMilliseconds,
                    cancellationToken);

                totalStopwatch.Stop();

                _logger.LogInformation(
                    "[EVALUATION_COMPLETED] SubmissionId={SubmissionId}, Score={Score}/{MaxScore}, Percentage={Percentage}%, Grade={Grade}, OcrMs={OcrMs}, EvalMs={EvalMs}, TotalMs={TotalMs}",
                    submissionId,
                    evaluationResult.TotalScore,
                    evaluationResult.MaxPossibleScore,
                    evaluationResult.Percentage,
                    evaluationResult.Grade,
                    ocrStopwatch.ElapsedMilliseconds,
                    evalStopwatch.ElapsedMilliseconds,
                    totalStopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "[RETRY_SCHEDULED] SubmissionId={SubmissionId} cancelled. Runtime will retry.",
                    submissionId);
                throw; // Let runtime handle retry
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 429)
            {
                // Rate limiting - re-enqueue with delay
                _logger.LogWarning(
                    "[RETRY_SCHEDULED] SubmissionId={SubmissionId} rate limited (429). Re-enqueueing.",
                    submissionId);

                if (message != null)
                {
                    await ReenqueueWithBackoffAsync(message, cancellationToken);
                }
                return; // Don't throw - we've handled it
            }
            catch (Azure.RequestFailedException ex) when (ex.Status >= 500)
            {
                // Server error - transient, re-enqueue
                _logger.LogWarning(ex,
                    "[RETRY_SCHEDULED] SubmissionId={SubmissionId} server error ({Status}). Re-enqueueing.",
                    submissionId, ex.Status);

                if (message != null)
                {
                    await HandleRetryOrFailAsync(message, $"Server error: {ex.Status}", cancellationToken);
                }
                else
                {
                    throw;
                }
            }
            catch (HttpRequestException ex)
            {
                // Network error - transient, retry
                _logger.LogWarning(ex,
                    "[RETRY_SCHEDULED] SubmissionId={SubmissionId} network error. Re-enqueueing.",
                    submissionId);

                if (message != null)
                {
                    await HandleRetryOrFailAsync(message, $"Network error: {ex.Message}", cancellationToken);
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[FAILED_FINAL] SubmissionId={SubmissionId} unexpected error: {Error}",
                    submissionId, ex.Message);

                if (message != null)
                {
                    await HandleRetryOrFailAsync(message, ex.Message, cancellationToken);
                }
                else
                {
                    throw; // No message context - let runtime handle
                }
            }
        }

        /// <summary>
        /// Handle retry logic: re-enqueue if under limit, else mark as failed.
        /// </summary>
        private async Task HandleRetryOrFailAsync(
            WrittenSubmissionProcessingMessage message,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            if (message.RetryCount < MaxRetries - 1)
            {
                await ReenqueueWithBackoffAsync(message, cancellationToken);
            }
            else
            {
                _logger.LogError(
                    "[FAILED_FINAL] SubmissionId={SubmissionId} max retries reached. Error={Error}",
                    message.WrittenSubmissionId, errorMessage);

                await _repository.UpdateStatusAsync(
                    message.WrittenSubmissionId,
                    WrittenSubmissionStatus.Failed,
                    errorMessage,
                    cancellationToken);
            }
        }

        /// <summary>
        /// Re-enqueue message with incremented retry count and visibility delay.
        /// Implements exponential backoff: 30s, 60s, 120s
        /// Also increments RetryCount in database for tracking.
        /// </summary>
        private async Task ReenqueueWithBackoffAsync(
            WrittenSubmissionProcessingMessage message,
            CancellationToken cancellationToken)
        {
            // Increment retry count in database for accurate tracking
            await _repository.IncrementRetryCountAsync(message.WrittenSubmissionId, cancellationToken);

            var newMessage = new WrittenSubmissionProcessingMessage
            {
                WrittenSubmissionId = message.WrittenSubmissionId,
                ExamId = message.ExamId,
                StudentId = message.StudentId,
                FilePaths = message.FilePaths,
                SubmittedAt = message.SubmittedAt,
                Priority = message.Priority,
                RetryCount = message.RetryCount + 1
            };

            // Exponential backoff: 30s, 60s, 120s
            var delaySeconds = 30 * (int)Math.Pow(2, message.RetryCount);
            var visibilityTimeout = TimeSpan.FromSeconds(delaySeconds);

            var queueClient = _queueServiceClient.GetQueueClient(QueueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            
            var messageJson = JsonSerializer.Serialize(newMessage);
            // Base64 encode for queue storage compatibility
            var encodedMessage = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(messageJson));

            await queueClient.SendMessageAsync(
                encodedMessage,
                visibilityTimeout: visibilityTimeout,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "[RETRY_SCHEDULED] SubmissionId={SubmissionId}, Retry={RetryCount}/{MaxRetries}, DelaySeconds={DelaySeconds}",
                message.WrittenSubmissionId, newMessage.RetryCount, MaxRetries, delaySeconds);
        }

        /// <summary>
        /// Save large OCR text to blob storage.
        /// </summary>
        private async Task<string> SaveTextToBlobAsync(
            Guid submissionId,
            string examId,
            string text,
            CancellationToken cancellationToken)
        {
            var containerName = "ocr-extracted-text";
            var blobPath = $"{examId}/{submissionId}/extracted-text.txt";

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var blobClient = containerClient.GetBlobClient(blobPath);

            using var stream = new System.IO.MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(text));
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);

            return $"{containerName}/{blobPath}";
        }

        /// <summary>
        /// Saves evaluation results as JSON to blob storage for permanent student access.
        /// This ensures students can view their detailed results anytime without data loss.
        /// </summary>
        private async Task<string> SaveEvaluationResultToBlobAsync(
            Guid submissionId,
            string examId,
            WrittenEvaluationResult result,
            CancellationToken cancellationToken)
        {
            var containerName = "evaluation-results";
            var blobPath = $"{examId}/{submissionId}/evaluation-result.json";

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var blobClient = containerClient.GetBlobClient(blobPath);

            // Transform result for better mobile app display
            var apiResult = TransformForApi(result);

            // Serialize evaluation result to JSON with pretty formatting for readability
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(apiResult, jsonOptions);

            using var stream = new System.IO.MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(json));
            
            // Set content type for JSON
            var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
            {
                HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
                {
                    ContentType = "application/json"
                }
            };
            
            await blobClient.UploadAsync(stream, uploadOptions, cancellationToken: cancellationToken);

            return $"{containerName}/{blobPath}";
        }

        /// <summary>
        /// Transform evaluation result for API - format extracted answers and parse rubric breakdown
        /// </summary>
        private object TransformForApi(WrittenEvaluationResult result)
        {
            var questionEvals = new List<object>();
            
            foreach (var q in result.QuestionEvaluations)
            {
                // Format extracted answer with line breaks
                var formattedAnswer = FormatExtractedAnswerWithLineBreaks(q.ExtractedAnswer);

                // Parse rubric breakdown from JSON string to object
                object rubricObj = new { };
                if (!string.IsNullOrEmpty(q.RubricBreakdown) && q.RubricBreakdown.Trim().StartsWith("{"))
                {
                    try
                    {
                        rubricObj = JsonSerializer.Deserialize<JsonElement>(q.RubricBreakdown);
                    }
                    catch
                    {
                        rubricObj = new { raw = q.RubricBreakdown };
                    }
                }

                questionEvals.Add(new
                {
                    questionId = q.QuestionId,
                    questionNumber = q.QuestionNumber,
                    questionText = q.QuestionText,
                    extractedAnswer = formattedAnswer,
                    modelAnswer = q.ModelAnswer,
                    maxScore = q.MaxScore,
                    awardedScore = q.AwardedScore,
                    feedback = q.Feedback,
                    rubricBreakdown = rubricObj,
                    evaluatedAt = q.EvaluatedAt,
                    isMcq = q.IsMcq
                });
            }

            return new
            {
                writtenSubmissionId = result.WrittenSubmissionId,
                examId = result.ExamId,
                studentId = result.StudentId,
                evaluatedAt = result.EvaluatedAt,
                summary = new
                {
                    totalScore = result.TotalScore,
                    maxPossibleScore = result.MaxPossibleScore,
                    percentage = result.Percentage,
                    grade = result.Grade
                },
                evaluationResult = new
                {
                    totalScore = result.TotalScore,
                    maxPossibleScore = result.MaxPossibleScore,
                    percentage = result.Percentage,
                    grade = result.Grade,
                    mcqScore = result.McqScore,
                    mcqMaxScore = result.McqMaxScore,
                    mcqCount = result.McqCount,
                    subjectiveScore = result.SubjectiveScore,
                    subjectiveMaxScore = result.SubjectiveMaxScore,
                    subjectiveCount = result.SubjectiveCount,
                    questionEvaluations = questionEvals
                }
            };
        }

        /// <summary>
        /// Format extracted answer with line breaks for better readability
        /// Detects natural breaks in mathematical solutions
        /// </summary>
        private string FormatExtractedAnswerWithLineBreaks(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Contains("\n"))
                return text; // Already has line breaks or empty

            // Add line breaks after common patterns in math solutions
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(\d+[a-z]*\s*=\s*[^=]+?)(?=\s+\d+[a-z]*\s*=)", "$1\n");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"([a-z]\s*=\s*[^=]+?)(?=\s+[a-z]\s*=)", "$1\n");
            
            return text;
        }

        /// <summary>
        /// Merges MCQ data from the database (populated by backend API) into the evaluation result.
        /// MCQ questions are evaluated by the backend and stored in WrittenSubmissions table.
        /// This method adds that data to the evaluation result blob.
        /// </summary>
        private void MergeMcqDataIntoEvaluationResult(
            WrittenSubmission submission,
            WrittenEvaluationResult evaluationResult)
        {
            // Parse MCQ answers JSON if present
            if (!string.IsNullOrEmpty(submission.McqAnswers))
            {
                try
                {
                    var mcqData = JsonSerializer.Deserialize<JsonElement>(submission.McqAnswers);
                    
                    // If McqAnswers contains questionEvaluations array, merge them
                    if (mcqData.ValueKind == JsonValueKind.Object && 
                        mcqData.TryGetProperty("questionEvaluations", out var questionsElement))
                    {
                        foreach (var q in questionsElement.EnumerateArray())
                        {
                            var questionNumber = q.TryGetProperty("questionNumber", out var qn) ? qn.GetInt32() : 0;
                            var questionId = q.TryGetProperty("questionId", out var qid) ? qid.GetString() ?? "" : "";
                            var questionText = q.TryGetProperty("questionText", out var qt) ? qt.GetString() ?? "" : "";
                            var extractedAnswer = q.TryGetProperty("extractedAnswer", out var ea) ? ea.GetString() ?? "" : "";
                            var modelAnswer = q.TryGetProperty("modelAnswer", out var ma) ? ma.GetString() ?? "" : "";
                            var awardedScore = q.TryGetProperty("awardedScore", out var aw) ? aw.GetDecimal() : 0;
                            var maxScore = q.TryGetProperty("maxScore", out var ms) ? ms.GetDecimal() : 0;
                            var feedback = q.TryGetProperty("feedback", out var fb) ? fb.GetString() ?? "" : "";
                            var isCorrect = awardedScore == maxScore;
                            
                            // Use feedback from database, or generate if not present
                            if (string.IsNullOrEmpty(feedback))
                            {
                                feedback = isCorrect ? "Correct!" : $"Incorrect. Correct answer: {modelAnswer}";
                            }
                            
                            // Check if this question already exists in evaluation result
                            var existingEval = evaluationResult.QuestionEvaluations
                                .Find(e => e.QuestionNumber == questionNumber || e.QuestionId == questionId);
                            
                            if (existingEval != null)
                            {
                                // UPDATE existing evaluation with MCQ data from database
                                existingEval.ExtractedAnswer = extractedAnswer;
                                existingEval.ModelAnswer = modelAnswer;
                                existingEval.AwardedScore = awardedScore;
                                existingEval.MaxScore = maxScore;
                                existingEval.Feedback = feedback;
                                existingEval.RubricBreakdown = ""; // No rubric for MCQ
                                existingEval.IsMcq = true;
                                existingEval.EvaluatedAt = DateTime.UtcNow;
                                
                                _logger.LogInformation(
                                    "[MCQ_UPDATE] Q{QuestionNumber}: Updated with DB data - Answer={Answer}, Score={Score}/{Max}",
                                    questionNumber, extractedAnswer, awardedScore, maxScore);
                            }
                            else
                            {
                                // Add new MCQ evaluation (no rubricBreakdown for MCQ)
                                evaluationResult.QuestionEvaluations.Add(new WrittenQuestionEvaluation
                                {
                                    Id = Guid.NewGuid(),
                                    WrittenSubmissionId = submission.Id,
                                    QuestionId = questionId,
                                    QuestionNumber = questionNumber,
                                    QuestionText = questionText,
                                    ExtractedAnswer = extractedAnswer,
                                    ModelAnswer = modelAnswer,
                                    MaxScore = maxScore,
                                    AwardedScore = awardedScore,
                                    Feedback = feedback,
                                    RubricBreakdown = "", // No rubric breakdown for MCQ
                                    EvaluatedAt = DateTime.UtcNow,
                                    IsMcq = true
                                });
                                
                                _logger.LogInformation(
                                    "[MCQ_ADD] Q{QuestionNumber}: Added from DB - Answer={Answer}, Score={Score}/{Max}",
                                    questionNumber, extractedAnswer, awardedScore, maxScore);
                            }
                        }
                    }
                    // If McqAnswers is just an array of evaluations
                    else if (mcqData.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var q in mcqData.EnumerateArray())
                        {
                            var questionNumber = q.TryGetProperty("questionNumber", out var qn) ? qn.GetInt32() : 0;
                            var questionId = q.TryGetProperty("questionId", out var qid) ? qid.GetString() ?? "" : "";
                            var questionText = q.TryGetProperty("questionText", out var qt) ? qt.GetString() ?? "" : "";
                            var extractedAnswer = q.TryGetProperty("extractedAnswer", out var ea) ? ea.GetString() ?? "" : 
                                                  q.TryGetProperty("studentAnswer", out var sa) ? sa.GetString() ?? "" : "";
                            var modelAnswer = q.TryGetProperty("modelAnswer", out var ma) ? ma.GetString() ?? "" :
                                              q.TryGetProperty("correctAnswer", out var ca) ? ca.GetString() ?? "" : "";
                            var awardedScore = q.TryGetProperty("awardedScore", out var aw) ? aw.GetDecimal() : 
                                               q.TryGetProperty("score", out var sc) ? sc.GetDecimal() : 0;
                            var maxScore = q.TryGetProperty("maxScore", out var ms) ? ms.GetDecimal() : 
                                           q.TryGetProperty("maxMarks", out var mm) ? mm.GetDecimal() : 1;
                            var feedback = q.TryGetProperty("feedback", out var fb) ? fb.GetString() ?? "" : "";
                            var isCorrect = awardedScore == maxScore;
                            
                            // Use feedback from database, or generate if not present
                            if (string.IsNullOrEmpty(feedback))
                            {
                                feedback = isCorrect ? "Correct!" : $"Incorrect. Correct answer: {modelAnswer}";
                            }
                            
                            var existingEval = evaluationResult.QuestionEvaluations
                                .Find(e => e.QuestionNumber == questionNumber || e.QuestionId == questionId);
                            
                            if (existingEval != null)
                            {
                                // UPDATE existing evaluation with MCQ data from database
                                existingEval.ExtractedAnswer = extractedAnswer;
                                existingEval.ModelAnswer = modelAnswer;
                                existingEval.AwardedScore = awardedScore;
                                existingEval.MaxScore = maxScore;
                                existingEval.Feedback = feedback;
                                existingEval.RubricBreakdown = ""; // No rubric for MCQ
                                existingEval.IsMcq = true;
                                existingEval.EvaluatedAt = DateTime.UtcNow;
                                
                                _logger.LogInformation(
                                    "[MCQ_UPDATE_ARR] Q{QuestionNumber}: Updated - Answer={Answer}, Score={Score}/{Max}",
                                    questionNumber, extractedAnswer, awardedScore, maxScore);
                            }
                            else
                            {
                                evaluationResult.QuestionEvaluations.Add(new WrittenQuestionEvaluation
                                {
                                    Id = Guid.NewGuid(),
                                    WrittenSubmissionId = submission.Id,
                                    QuestionId = questionId,
                                    QuestionNumber = questionNumber,
                                    QuestionText = questionText,
                                    ExtractedAnswer = extractedAnswer,
                                    ModelAnswer = modelAnswer,
                                    MaxScore = maxScore,
                                    AwardedScore = awardedScore,
                                    Feedback = feedback,
                                    RubricBreakdown = "", // No rubric breakdown for MCQ
                                    EvaluatedAt = DateTime.UtcNow,
                                    IsMcq = true
                                });
                                
                                _logger.LogInformation(
                                    "[MCQ_ADD_ARR] Q{QuestionNumber}: Added - Answer={Answer}, Score={Score}/{Max}",
                                    questionNumber, extractedAnswer, awardedScore, maxScore);
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, 
                        "[MCQ_MERGE] Failed to parse McqAnswers JSON for SubmissionId={SubmissionId}",
                        submission.Id);
                }
            }

            // Update MCQ scores from database values
            if (submission.McqScore.HasValue)
            {
                evaluationResult.McqScore = submission.McqScore.Value;
            }
            if (submission.McqTotalMarks.HasValue)
            {
                evaluationResult.McqMaxScore = submission.McqTotalMarks.Value;
            }
            
            // Recalculate MCQ count from question evaluations
            var mcqCount = evaluationResult.QuestionEvaluations.Count(e => e.IsMcq);
            if (mcqCount > 0)
            {
                evaluationResult.McqCount = mcqCount;
            }

            // Recalculate totals if MCQ data was added
            if (submission.McqScore.HasValue || submission.McqTotalMarks.HasValue)
            {
                // Recalculate total score including MCQ
                evaluationResult.TotalScore = evaluationResult.QuestionEvaluations.Sum(e => e.AwardedScore);
                evaluationResult.MaxPossibleScore = evaluationResult.QuestionEvaluations.Sum(e => e.MaxScore);
                
                if (evaluationResult.MaxPossibleScore > 0)
                {
                    evaluationResult.Percentage = Math.Round(
                        (evaluationResult.TotalScore / evaluationResult.MaxPossibleScore) * 100, 2);
                }
                
                // Recalculate subjective scores
                var subjectiveEvals = evaluationResult.QuestionEvaluations.Where(e => !e.IsMcq).ToList();
                evaluationResult.SubjectiveScore = subjectiveEvals.Sum(e => e.AwardedScore);
                evaluationResult.SubjectiveMaxScore = subjectiveEvals.Sum(e => e.MaxScore);
                evaluationResult.SubjectiveCount = subjectiveEvals.Count;
            }

            // Sort question evaluations by question number
            evaluationResult.QuestionEvaluations = evaluationResult.QuestionEvaluations
                .OrderBy(e => e.QuestionNumber)
                .ToList();

            _logger.LogInformation(
                "[MCQ_MERGE_COMPLETE] SubmissionId={SubmissionId}, McqScore={McqScore}/{McqMax}, SubjScore={SubjScore}/{SubjMax}, Total={Total}/{Max}",
                submission.Id, 
                evaluationResult.McqScore, evaluationResult.McqMaxScore,
                evaluationResult.SubjectiveScore, evaluationResult.SubjectiveMaxScore,
                evaluationResult.TotalScore, evaluationResult.MaxPossibleScore);
        }

        /// <summary>
        /// Evaluates submission using V2 evaluation engines (subject-specific routing)
        /// </summary>
        private async Task<WrittenEvaluationResult> EvaluateWithV2Async(
            WrittenSubmission submission,
            string extractedText,
            System.Collections.Generic.List<ExamQuestionWithRubric> questions,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[V2_EVALUATION_START] SubmissionId={SubmissionId}, QuestionCount={QuestionCount}",
                submission.Id, questions.Count);

            var result = new WrittenEvaluationResult
            {
                QuestionEvaluations = new System.Collections.Generic.List<WrittenQuestionEvaluation>()
            };

            decimal totalScore = 0;
            decimal maxPossibleScore = 0;

            foreach (var question in questions)
            {
                try
                {
                    // Create evaluation context for V2 engine
                    var context = new EvaluationContext
                    {
                        QuestionText = question.QuestionText,
                        StudentAnswer = extractedText, // Full text for now - V2 engine will extract relevant part
                        ModelAnswer = question.ModelAnswer,
                        MaxMarks = (double)question.MaxScore,
                        Subject = ParseSubjectCategory(question.Subject ?? "Unknown")
                    };

                    _logger.LogInformation(
                        "[V2_EVALUATE_QUESTION] Q{QuestionNumber}, Subject={Subject}",
                        question.QuestionNumber, context.Subject);

                    // Route to appropriate V2 engine
                    var engineResult = await _subjectRouter.RouteAndEvaluateAsync(context, cancellationToken);

                    var evaluation = new WrittenQuestionEvaluation
                    {
                        QuestionId = question.QuestionId,
                        QuestionNumber = question.QuestionNumber,
                        QuestionText = question.QuestionText,
                        ExtractedAnswer = extractedText,
                        ModelAnswer = question.ModelAnswer,
                        MaxScore = question.MaxScore,
                        AwardedScore = (decimal)engineResult.MarksAwarded,
                        Feedback = engineResult.StudentFeedback,
                        RubricBreakdown = JsonSerializer.Serialize(engineResult.StepWiseBreakdown),
                        EvaluatedAt = DateTime.UtcNow,
                        IsMcq = false
                    };

                    result.QuestionEvaluations.Add(evaluation);

                    totalScore += evaluation.AwardedScore;
                    maxPossibleScore += evaluation.MaxScore;

                    _logger.LogInformation(
                        "[V2_QUESTION_EVALUATED] Q{QuestionNumber}: Score={Score}/{Max}, Engine={Engine}",
                        question.QuestionNumber, evaluation.AwardedScore, evaluation.MaxScore, engineResult.ProcessedBy);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[V2_QUESTION_FAILED] Q{QuestionNumber} failed: {Error}",
                        question.QuestionNumber, ex.Message);

                    // Add failed evaluation
                    result.QuestionEvaluations.Add(new WrittenQuestionEvaluation
                    {
                        QuestionId = question.QuestionId,
                        QuestionNumber = question.QuestionNumber,
                        QuestionText = question.QuestionText,
                        ExtractedAnswer = extractedText,
                        ModelAnswer = question.ModelAnswer,
                        MaxScore = question.MaxScore,
                        AwardedScore = 0,
                        Feedback = $"Evaluation failed: {ex.Message}",
                        RubricBreakdown = "[]",
                        EvaluatedAt = DateTime.UtcNow,
                        IsMcq = false
                    });

                    maxPossibleScore += question.MaxScore;
                }
            }

            // Calculate final scores
            result.TotalScore = totalScore;
            result.MaxPossibleScore = maxPossibleScore;
            result.SubjectiveScore = totalScore;
            result.SubjectiveMaxScore = maxPossibleScore;
            result.SubjectiveCount = questions.Count;
            result.Percentage = maxPossibleScore > 0 ? Math.Round((totalScore / maxPossibleScore) * 100, 2) : 0;
            result.Grade = CalculateGrade(result.Percentage);

            _logger.LogInformation(
                "[V2_EVALUATION_COMPLETE] SubmissionId={SubmissionId}, Score={Score}/{Max}, Percentage={Percentage}%",
                submission.Id, totalScore, maxPossibleScore, result.Percentage);

            return result;
        }

        private string CalculateGrade(decimal percentage)
        {
            if (percentage >= 90) return "A+";
            if (percentage >= 80) return "A";
            if (percentage >= 70) return "B";
            if (percentage >= 60) return "C";
            if (percentage >= 50) return "D";
            return "F";
        }

        private SubjectCategory ParseSubjectCategory(string subject)
        {
            return subject?.ToLower() switch
            {
                "mathematics" or "math" or "maths" => SubjectCategory.Mathematics,
                "physics" => SubjectCategory.Physics,
                "chemistry" => SubjectCategory.Chemistry,
                "biology" => SubjectCategory.Biology,
                "social science" or "socialscience" or "social" => SubjectCategory.SocialScience,
                "english" => SubjectCategory.English,
                "hindi" => SubjectCategory.Hindi,
                _ => SubjectCategory.Unknown
            };
        }
    }
}
