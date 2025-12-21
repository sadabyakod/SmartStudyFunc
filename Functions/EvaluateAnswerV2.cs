using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services.Evaluation;
using System;
using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// Enhanced EvaluateAnswer with Subject-Specific Routing
    /// Routes to Mathematics, Physics/Chemistry, Biology/Social, or Language engines
    /// CRITICAL: OpenAI does NOT decide marks for Math/Science - rule-based engines do
    /// </summary>
    public class EvaluateAnswerV2
    {
        private readonly ILogger<EvaluateAnswerV2> _logger;
        private readonly ISubjectRouter _subjectRouter;
        private readonly string _connectionString;

        public EvaluateAnswerV2(
            ILogger<EvaluateAnswerV2> logger,
            ISubjectRouter subjectRouter)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _subjectRouter = subjectRouter ?? throw new ArgumentNullException(nameof(subjectRouter));
            
            _connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING") 
                ?? Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__SqlDb")
                ?? throw new InvalidOperationException("No SQL connection string found");
            
            _logger.LogInformation("EvaluateAnswerV2 initialized with SubjectRouter");
        }

        [Function("EvaluateAnswerV2")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "answers/evaluate/v2")] HttpRequest req,
            CancellationToken ct)
        {
            _logger.LogInformation("EvaluateAnswerV2 (Subject-Routed) triggered");

            try
            {
                // Parse request
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult(new { Error = "Request body is required" });
                }

                var request = JsonConvert.DeserializeObject<EvaluateAnswerRequest>(requestBody);
                if (request == null)
                {
                    return new BadRequestObjectResult(new { Error = "Invalid request format" });
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(request.ExamId) || 
                    request.QuestionId == Guid.Empty || 
                    string.IsNullOrWhiteSpace(request.StudentAnswerText))
                {
                    return new BadRequestObjectResult(new { Error = "ExamId, QuestionId, and StudentAnswerText are required" });
                }

                _logger.LogInformation(
                    "Evaluating: ExamId={ExamId}, QuestionId={QuestionId}",
                    request.ExamId, request.QuestionId);

                // Load question details from database
                var questionQuery = @"
                    SELECT 
                        QuestionText,
                        ModelAnswer as IdealAnswer,
                        MaxScore as Marks,
                        Keywords,
                        Subject,
                        QuestionType,
                        ClassLevel
                    FROM ExamQuestions
                    WHERE Id = @QuestionId";

                dynamic? questionData;
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync(ct);
                    questionData = (await conn.QueryAsync<dynamic>(questionQuery, new { QuestionId = request.QuestionId }))
                        .FirstOrDefault();
                }

                if (questionData == null)
                {
                    return new NotFoundObjectResult(new { Error = $"Question not found: {request.QuestionId}" });
                }

                // Extract question details
                var questionText = (string?)questionData.QuestionText ?? string.Empty;
                var idealAnswer = (string?)questionData.IdealAnswer ?? string.Empty;
                var maxMarks = (double)(decimal)questionData.Marks;
                var keywordsJson = (string?)questionData.Keywords ?? "[]";
                
                // Handle Keywords - could be JSON array or comma-separated string
                string[] keywords;
                if (keywordsJson.StartsWith("["))
                {
                    // JSON array format
                    keywords = JsonConvert.DeserializeObject<string[]>(keywordsJson) ?? Array.Empty<string>();
                }
                else
                {
                    // Comma-separated string format
                    keywords = string.IsNullOrWhiteSpace(keywordsJson)
                        ? Array.Empty<string>()
                        : keywordsJson.Split(',').Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToArray();
                }
                
                var subjectStr = (string?)questionData.Subject ?? "Unknown";
                var questionTypeStr = (string?)questionData.QuestionType ?? "ShortAnswer";
                var classLevel = questionData.ClassLevel != null ? (int)questionData.ClassLevel : 10;

                // Parse subject and type
                Enum.TryParse<SubjectCategory>(subjectStr, true, out var subject);
                Enum.TryParse<QuestionType>(questionTypeStr, true, out var questionType);

                _logger.LogInformation(
                    "Question loaded: Subject={Subject}, Type={Type}, MaxMarks={MaxMarks}, ClassLevel={ClassLevel}",
                    subject, questionType, maxMarks, classLevel);

                // Build evaluation context
                var context = new EvaluationContext
                {
                    QuestionId = request.QuestionId.ToString(),
                    QuestionText = questionText,
                    StudentAnswer = request.StudentAnswerText,
                    ModelAnswer = idealAnswer,
                    MaxMarks = maxMarks,
                    Keywords = keywords.ToList(),
                    Subject = subject,
                    Type = questionType,
                    ClassLevel = classLevel,
                    SyllabusReference = $"syllabus/class-{classLevel}/{subjectStr.ToLowerInvariant()}.txt"
                };

                // Route to appropriate evaluation engine
                var engineResult = await _subjectRouter.RouteAndEvaluateAsync(context, ct);

                _logger.LogInformation(
                    "Evaluation complete: Engine={Engine}, Marks={Marks}/{Max}, Confidence={Confidence:F2}, NeedsReview={Review}",
                    engineResult.ProcessedBy, engineResult.MarksAwarded, engineResult.MaxMarks,
                    engineResult.ConfidenceScore, engineResult.NeedsReview);

                // Save evaluation to database
                var evaluationId = await SaveEvaluationAsync(
                    request,
                    engineResult,
                    ct);

                // Build response
                var percentage = engineResult.MaxMarks > 0
                    ? Math.Round((engineResult.MarksAwarded / engineResult.MaxMarks) * 100, 1)
                    : 0;

                var response = new EvaluateAnswerResponse
                {
                    Success = true,
                    EvaluationId = evaluationId,
                    ExamId = request.ExamId,
                    QuestionId = request.QuestionId,
                    Score = engineResult.MarksAwarded,
                    MaxMarks = (int)engineResult.MaxMarks,
                    Percentage = percentage,
                    Feedback = engineResult.StudentFeedback,
                    Strengths = string.Join("; ", engineResult.Strengths),
                    Improvements = string.Join("; ", engineResult.Improvements),
                    KeywordsMatched = engineResult.MatchedKeywords,
                    MissingKeywords = engineResult.MissingKeywords,
                    UsedFallback = false, // New engine system
                    StepWiseBreakdown = engineResult.StepWiseBreakdown,
                    IsComplete = !engineResult.NeedsReview,
                    CompletionStatus = engineResult.NeedsReview ? "NeedsReview" : "Complete"
                };

                // Add metadata to response
                var metadata = new
                {
                    EvaluationEngine = engineResult.ProcessedBy,
                    ConfidenceScore = engineResult.ConfidenceScore,
                    NeedsTeacherReview = engineResult.NeedsReview,
                    EvaluationReason = engineResult.EvaluationReason,
                    AuditTrail = engineResult.AuditTrail
                };

                return new OkObjectResult(new
                {
                    response.Success,
                    response.EvaluationId,
                    response.ExamId,
                    response.QuestionId,
                    response.Score,
                    response.MaxMarks,
                    response.Percentage,
                    response.Feedback,
                    response.Strengths,
                    response.Improvements,
                    response.KeywordsMatched,
                    response.MissingKeywords,
                    response.StepWiseBreakdown,
                    response.IsComplete,
                    response.CompletionStatus,
                    Metadata = metadata
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Evaluation operation cancelled");
                return new StatusCodeResult(499);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Evaluation failed");
                return new ObjectResult(new { Error = "Evaluation failed", Details = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Saves evaluation result to database
        /// </summary>
        private async Task<int> SaveEvaluationAsync(
            EvaluateAnswerRequest request,
            EvaluationEngineResult result,
            CancellationToken ct)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Create rubric breakdown JSON
            var rubricBreakdown = JsonConvert.SerializeObject(new
            {
                EvaluationEngine = result.ProcessedBy,
                ConfidenceScore = result.ConfidenceScore,
                NeedsReview = result.NeedsReview,
                EvaluationReason = result.EvaluationReason,
                KeywordsMatched = result.MatchedKeywords,
                MissingKeywords = result.MissingKeywords,
                Strengths = result.Strengths,
                Improvements = result.Improvements,
                StepWiseBreakdown = result.StepWiseBreakdown,
                AuditTrail = result.AuditTrail
            });

            // Determine or create WrittenSubmissionId
            var submissionId = request.WrittenSubmissionId ?? Guid.NewGuid();

            if (request.WrittenSubmissionId == null || request.WrittenSubmissionId == Guid.Empty)
            {
                // Create new submission record
                var createSubmissionQuery = @"
                    INSERT INTO WrittenSubmissions (
                        Id, ExamId, StudentId, FilePaths, Status, 
                        TotalScore, MaxPossibleScore, Percentage, 
                        SubmittedAt, EvaluatedAt
                    )
                    VALUES (
                        @Id, @ExamId, 'API-USER', '[]', 3,
                        @TotalScore, @MaxScore, @Percentage,
                        GETUTCDATE(), GETUTCDATE()
                    )";

                await conn.ExecuteAsync(createSubmissionQuery, new
                {
                    Id = submissionId,
                    ExamId = request.ExamId,
                    TotalScore = (decimal)result.MarksAwarded,
                    MaxScore = (decimal)result.MaxMarks,
                    Percentage = Math.Round((result.MarksAwarded / result.MaxMarks) * 100, 2)
                });

                _logger.LogInformation("Created WrittenSubmission {SubmissionId}", submissionId);
            }

            // Insert evaluation record
            var insertQuery = @"
                INSERT INTO WrittenQuestionEvaluations (
                    Id, WrittenSubmissionId, QuestionId, QuestionNumber, 
                    ExtractedAnswer, ModelAnswer, MaxScore, AwardedScore, 
                    Feedback, RubricBreakdown, EvaluatedAt
                )
                VALUES (
                    NEWID(), @WrittenSubmissionId, @QuestionId, 1,
                    @ExtractedAnswer, @ModelAnswer, @MaxScore, @AwardedScore,
                    @Feedback, @RubricBreakdown, GETUTCDATE()
                );
                SELECT CAST(@@ROWCOUNT AS INT);";

            var evaluationId = await conn.ExecuteScalarAsync<int>(insertQuery, new
            {
                WrittenSubmissionId = submissionId,
                QuestionId = request.QuestionId,
                ExtractedAnswer = request.StudentAnswerText,
                ModelAnswer = string.Empty, // Model answer already in evaluation
                MaxScore = (decimal)result.MaxMarks,
                AwardedScore = (decimal)result.MarksAwarded,
                Feedback = result.StudentFeedback,
                RubricBreakdown = rubricBreakdown
            });

            return evaluationId;
        }
    }
}
