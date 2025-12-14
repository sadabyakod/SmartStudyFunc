using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services;
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
    public class EvaluateAnswer
    {
        private readonly ILogger<EvaluateAnswer> _logger;
        private readonly AiScoringService _scoringService;
        private readonly string _connectionString;

        public EvaluateAnswer(
            ILogger<EvaluateAnswer> logger,
            AiScoringService scoringService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scoringService = scoringService ?? throw new ArgumentNullException(nameof(scoringService));
            
            _connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING") 
                ?? Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__SqlDb")
                ?? throw new InvalidOperationException("No SQL connection string found in environment variables");
            
            _logger.LogInformation("EvaluateAnswer constructor initialized successfully");
        }

        [Function("EvaluateAnswer")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "answers/evaluate")] HttpRequest req,
            CancellationToken ct)
        {
            _logger.LogInformation("EvaluateAnswer function triggered");

            // Check if services are initialized
            if (_scoringService == null)
            {
                _logger.LogError("AiScoringService is null");
                return new ObjectResult(new { Error = "Service initialization failed", Details = "_scoringService is null" }) { StatusCode = 500 };
            }

            if (string.IsNullOrEmpty(_connectionString))
            {
                _logger.LogError("Connection string is null or empty");
                return new ObjectResult(new { Error = "Configuration error", Details = "Connection string not set" }) { StatusCode = 500 };
            }

            try
            {
                // Parse request body
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult(new { Error = "Request body is required" });
                }

                EvaluateAnswerRequest? request;
                try
                {
                    request = JsonConvert.DeserializeObject<EvaluateAnswerRequest>(requestBody);
                }
                catch (JsonException ex)
                {
                    return new BadRequestObjectResult(new { Error = "Invalid JSON format", Details = ex.Message });
                }

                if (request == null)
                {
                    return new BadRequestObjectResult(new { Error = "Request deserialization failed" });
                }

                // Validate required fields
                if (string.IsNullOrWhiteSpace(request.ExamId))
                {
                    return new BadRequestObjectResult(new { Error = "ExamId is required" });
                }

                if (request.QuestionId == Guid.Empty)
                {
                    return new BadRequestObjectResult(new { Error = "QuestionId is required and must be a valid GUID" });
                }

                if (string.IsNullOrWhiteSpace(request.StudentAnswerText))
                {
                    return new BadRequestObjectResult(new { Error = "StudentAnswerText is required" });
                }

                _logger.LogInformation("Evaluating answer: ExamId={ExamId}, QuestionId={QuestionId}",
                    request.ExamId, request.QuestionId);

                // Load question details from database
                var questionQuery = @"
                    SELECT 
                        ModelAnswer as IdealAnswer,
                        MaxScore as Marks,
                        Keywords
                    FROM ExamQuestions
                    WHERE Id = @QuestionId";

                dynamic? questionData;
                try
                {
                    using (var conn = new SqlConnection(_connectionString))
                    {
                        await conn.OpenAsync(ct);
                        questionData = (await conn.QueryAsync<dynamic>(questionQuery, new { QuestionId = request.QuestionId }))
                            .FirstOrDefault();
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Database query failed for QuestionId={QuestionId}", request.QuestionId);
                    return new ObjectResult(new { Error = "Database query failed", Details = dbEx.Message }) { StatusCode = 500 };
                }

                if (questionData == null)
                {
                    return new NotFoundObjectResult(new
                    {
                        Error = $"Question not found with Id={request.QuestionId}"
                    });
                }
                var idealAnswer = (string)questionData.IdealAnswer ?? string.Empty;
                var maxMarks = (int)(decimal)questionData.Marks;
                var keywordsJson = (string)questionData.Keywords ?? "[]";
                var keywords = JsonConvert.DeserializeObject<string[]>(keywordsJson) ?? Array.Empty<string>();

                _logger.LogInformation("Question loaded: MaxMarks={MaxMarks}, Keywords={Keywords}",
                    maxMarks, keywords.Length);

                // Call AI scoring service
                ScoringResult scoringResult;
                try
                {
                    scoringResult = await _scoringService.ScoreAsync(
                        request.StudentAnswerText,
                        idealAnswer,
                        maxMarks,
                        keywords,
                        ct);
                }
                catch (Exception aiEx)
                {
                    _logger.LogError(aiEx, "AI scoring failed for QuestionId={QuestionId}", request.QuestionId);
                    return new ObjectResult(new { Error = "AI scoring failed", Details = aiEx.Message }) { StatusCode = 500 };
                }

                _logger.LogInformation("AI scoring complete: Score={Score}/{MaxMarks}, UsedFallback={Fallback}",
                    scoringResult.Score, scoringResult.MaxMarks, scoringResult.UsedFallback);

                // Save evaluation to database using WrittenQuestionEvaluations table
                var insertQuery = @"
                    INSERT INTO WrittenQuestionEvaluations (
                        Id, WrittenSubmissionId, QuestionId, QuestionNumber, 
                        ExtractedAnswer, ModelAnswer, MaxScore, AwardedScore, 
                        Feedback, RubricBreakdown, EvaluatedAt
                    )
                    VALUES (
                        NEWID(), @WrittenSubmissionId, @QuestionId, @QuestionNumber,
                        @ExtractedAnswer, @ModelAnswer, @MaxScore, @AwardedScore,
                        @Feedback, @RubricBreakdown, GETUTCDATE()
                    );
                    SELECT CAST(@@ROWCOUNT AS INT);";

                int evaluationId;
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync(ct);
                    
                    // Create rubric breakdown JSON
                    var rubricBreakdown = JsonConvert.SerializeObject(new
                    {
                        KeywordsMatched = scoringResult.KeywordsMatched,
                        MissingKeywords = scoringResult.MissingKeywords,
                        Strengths = scoringResult.Strengths,
                        ImprovementSuggestion = scoringResult.ImprovementSuggestion
                    });
                    
                    evaluationId = await conn.ExecuteScalarAsync<int>(
                        insertQuery,
                        new
                        {
                            WrittenSubmissionId = request.WrittenSubmissionId ?? Guid.NewGuid(), // Generate new GUID if not provided
                            QuestionId = request.QuestionId,
                            QuestionNumber = 1, // Would come from actual question
                            ExtractedAnswer = request.StudentAnswerText,
                            ModelAnswer = idealAnswer,
                            MaxScore = (decimal)scoringResult.MaxMarks,
                            AwardedScore = (decimal)scoringResult.Score,
                            Feedback = scoringResult.Feedback,
                            RubricBreakdown = rubricBreakdown
                        });
                    
                    // Update WrittenSubmissions status if WrittenSubmissionId was provided
                    if (request.WrittenSubmissionId.HasValue && request.WrittenSubmissionId.Value != Guid.Empty)
                    {
                        var updateStatusQuery = @"
                            UPDATE WrittenSubmissions 
                            SET Status = 3,  -- 3 = Completed
                                TotalScore = ISNULL(TotalScore, 0) + @AwardedScore,
                                MaxPossibleScore = ISNULL(MaxPossibleScore, 0) + @MaxScore,
                                Percentage = CASE 
                                    WHEN (ISNULL(MaxPossibleScore, 0) + @MaxScore) > 0 
                                    THEN ((ISNULL(TotalScore, 0) + @AwardedScore) / (ISNULL(MaxPossibleScore, 0) + @MaxScore)) * 100 
                                    ELSE 0 
                                END,
                                EvaluatedAt = GETUTCDATE()
                            WHERE Id = @WrittenSubmissionId";
                        
                        await conn.ExecuteAsync(updateStatusQuery, new
                        {
                            WrittenSubmissionId = request.WrittenSubmissionId.Value,
                            AwardedScore = (decimal)scoringResult.Score,
                            MaxScore = (decimal)scoringResult.MaxMarks
                        });
                        
                        _logger.LogInformation("WrittenSubmission {SubmissionId} status updated to Completed", request.WrittenSubmissionId.Value);
                    }
                }

                _logger.LogInformation("Evaluation saved successfully, RowCount={EvaluationId}", evaluationId);

                // Build response
                var percentage = Math.Round((scoringResult.Score / scoringResult.MaxMarks) * 100, 1);

                var response = new EvaluateAnswerResponse
                {
                    Success = true,
                    EvaluationId = evaluationId,
                    ExamId = request.ExamId,
                    QuestionId = request.QuestionId,
                    Score = scoringResult.Score,
                    MaxMarks = scoringResult.MaxMarks,
                    Percentage = percentage,
                    Feedback = scoringResult.Feedback,
                    Strengths = string.Join("; ", scoringResult.Strengths),
                    Improvements = scoringResult.ImprovementSuggestion,
                    KeywordsMatched = scoringResult.KeywordsMatched,
                    MissingKeywords = scoringResult.MissingKeywords,
                    UsedFallback = scoringResult.UsedFallback
                };

                return new OkObjectResult(response);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Evaluation operation was cancelled");
                return new StatusCodeResult(499); // Client Closed Request
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Evaluation failed");
                return new ObjectResult(new { Error = "Evaluation processing failed", Details = ex.ToString() })
                {
                    StatusCode = 500
                };
            }
        }
    }
}
