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
            _logger = logger;
            _scoringService = scoringService;
            _connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                ?? throw new InvalidOperationException("SQL_CONNECTION_STRING environment variable is not set");
        }

        [Function("EvaluateAnswer")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "answers/evaluate")] HttpRequest req,
            CancellationToken ct)
        {
            _logger.LogInformation("EvaluateAnswer function triggered");

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
                if (request.ExamId <= 0)
                {
                    return new BadRequestObjectResult(new { Error = "ExamId must be a positive integer" });
                }

                if (request.QuestionId <= 0)
                {
                    return new BadRequestObjectResult(new { Error = "QuestionId must be a positive integer" });
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
                        IdealAnswer,
                        Marks,
                        Keywords
                    FROM GeneratedQuestions
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
                    return new NotFoundObjectResult(new
                    {
                        Error = $"Question not found with Id={request.QuestionId}"
                    });
                }
                var idealAnswer = (string)questionData.IdealAnswer ?? string.Empty;
                var maxMarks = (int)questionData.Marks;
                var keywordsJson = (string)questionData.Keywords ?? "[]";
                var keywords = JsonConvert.DeserializeObject<string[]>(keywordsJson) ?? Array.Empty<string>();

                _logger.LogInformation("Question loaded: MaxMarks={MaxMarks}, Keywords={Keywords}",
                    maxMarks, keywords.Length);

                // Call AI scoring service
                var scoringResult = await _scoringService.ScoreAsync(
                    request.StudentAnswerText,
                    idealAnswer,
                    maxMarks,
                    keywords,
                    ct);

                _logger.LogInformation("AI scoring complete: Score={Score}/{MaxMarks}, UsedFallback={Fallback}",
                    scoringResult.Score, scoringResult.MaxMarks, scoringResult.UsedFallback);

                // Save evaluation to database
                var insertQuery = @"
                    INSERT INTO EvaluatedAnswers (
                        ExamId, QuestionId, StudentAnswer, ExtractedText, IdealAnswer,
                        Score, MaxMarks, Feedback, KeywordsMatched, MissingKeywords,
                        Strengths, ImprovementSuggestions, BlobPath, EvaluatedOn
                    )
                    VALUES (
                        @ExamId, @QuestionId, @StudentAnswer, @ExtractedText, @IdealAnswer,
                        @Score, @MaxMarks, @Feedback, @KeywordsMatched, @MissingKeywords,
                        @Strengths, @ImprovementSuggestions, @BlobPath, GETUTCDATE()
                    );
                    SELECT CAST(SCOPE_IDENTITY() AS INT);";

                int evaluationId;
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync(ct);
                    evaluationId = await conn.ExecuteScalarAsync<int>(
                        insertQuery,
                        new
                        {
                            request.ExamId,
                            request.QuestionId,
                            StudentAnswer = request.StudentAnswerText,
                            request.ExtractedText,
                            IdealAnswer = idealAnswer,
                            scoringResult.Score,
                            scoringResult.MaxMarks,
                            scoringResult.Feedback,
                            KeywordsMatched = JsonConvert.SerializeObject(scoringResult.KeywordsMatched),
                            MissingKeywords = JsonConvert.SerializeObject(scoringResult.MissingKeywords),
                            Strengths = JsonConvert.SerializeObject(scoringResult.Strengths),
                            ImprovementSuggestions = scoringResult.ImprovementSuggestion,
                            request.BlobPath
                        });
                }

                _logger.LogInformation("Evaluation saved with Id={EvaluationId}", evaluationId);

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
                return new ObjectResult(new { Error = "Evaluation processing failed", Details = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }
    }
}
