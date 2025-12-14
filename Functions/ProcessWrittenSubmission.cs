using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services;

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
        private readonly IWrittenAnswerEvaluationService _evaluationService;
        private readonly IWrittenSubmissionRepository _repository;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly ILogger<ProcessWrittenSubmission> _logger;

        private const int MaxRetries = 3;
        private const string QueueName = "written-submission-processing";
        private const int MaxTextLengthForSql = 100000; // 100KB threshold

        public ProcessWrittenSubmission(
            IGoogleVisionOcrService ocrService,
            IWrittenAnswerEvaluationService evaluationService,
            IWrittenSubmissionRepository repository,
            BlobServiceClient blobServiceClient,
            QueueServiceClient queueServiceClient,
            ILogger<ProcessWrittenSubmission> logger)
        {
            _ocrService = ocrService;
            _evaluationService = evaluationService;
            _repository = repository;
            _blobServiceClient = blobServiceClient;
            _queueServiceClient = queueServiceClient;
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
                    // Check if evaluation started recently (within 10 minutes) - if so, skip
                    if (submission.EvaluationStartedAt.HasValue && 
                        (DateTime.UtcNow - submission.EvaluationStartedAt.Value).TotalMinutes < 10)
                    {
                        _logger.LogInformation(
                            "[QUEUE_RECEIVED] SubmissionId={SubmissionId} is Evaluating (started {Minutes:F1}m ago). Skipping.",
                            submissionId, (DateTime.UtcNow - submission.EvaluationStartedAt.Value).TotalMinutes);
                        return;
                    }
                    // Otherwise, evaluation may have stalled - allow retry
                    _logger.LogWarning(
                        "[QUEUE_RECEIVED] SubmissionId={SubmissionId} was Evaluating but stalled. Reprocessing.",
                        submissionId);
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

                var ocrResult = await _ocrService.ExtractTextFromBlobsAsync(
                    message.FilePaths,
                    submissionId,
                    cancellationToken);

                ocrStopwatch.Stop();

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

                // Save OCR results
                var extractedText = ocrResult.NormalizedText;
                var extractedTextJson = JsonSerializer.Serialize(ocrResult.Pages);
                string? textBlobPath = null;

                // Store large text in blob storage
                if (extractedText.Length > MaxTextLengthForSql)
                {
                    textBlobPath = await SaveTextToBlobAsync(
                        submissionId, examId, extractedText, cancellationToken);
                    
                    _logger.LogInformation(
                        "[OCR_COMPLETED] SubmissionId={SubmissionId}, Chars={CharCount} (stored in blob), Confidence={Confidence:P2}, DurationMs={Duration}",
                        submissionId, extractedText.Length, ocrResult.AverageConfidence, ocrStopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation(
                        "[OCR_COMPLETED] SubmissionId={SubmissionId}, Chars={CharCount}, Confidence={Confidence:P2}, DurationMs={Duration}",
                        submissionId, extractedText.Length, ocrResult.AverageConfidence, ocrStopwatch.ElapsedMilliseconds);
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
                var questions = await _repository.GetExamQuestionsWithRubricsAsync(
                    examId, cancellationToken);

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

                var evaluationResult = await _evaluationService.EvaluateSubmissionAsync(
                    submission,
                    extractedText,
                    questions,
                    cancellationToken);

                evalStopwatch.Stop();

                // ════════════════════════════════════════════════════════════════
                // PHASE 5: Save Results and Update Status
                // ════════════════════════════════════════════════════════════════
                await _repository.SaveEvaluationResultAsync(
                    evaluationResult,
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
    }
}
