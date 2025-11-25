using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// Azure Function to retrieve evaluation results
    /// Supports fetching by exam, question, or specific evaluation ID
    /// </summary>
    public class GetEvaluationResults
    {
        private readonly ILogger<GetEvaluationResults> _logger;
        private readonly SqlDb _sqlDb;

        public GetEvaluationResults(ILogger<GetEvaluationResults> logger)
        {
            _logger = logger;
            var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? throw new InvalidOperationException("SqlConnectionString not configured");
            _sqlDb = new SqlDb(connectionString);
        }

        /// <summary>
        /// Get all evaluations for an exam
        /// GET /evaluations/exam/{examId}
        /// </summary>
        [Function("GetEvaluationsByExam")]
        public async Task<IActionResult> GetByExam(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evaluations/exam/{examId}")] HttpRequest req,
            int examId)
        {
            _logger.LogInformation($"Fetching evaluations for exam: {examId}");

            try
            {
                var query = @"
                    SELECT 
                        e.Id,
                        e.ExamId,
                        e.QuestionId,
                        q.Question AS QuestionText,
                        e.Score,
                        e.MaxMarks,
                        e.Feedback,
                        e.KeywordsMatched,
                        e.MissingKeywords,
                        e.CreatedOn
                    FROM EvaluatedAnswers e
                    INNER JOIN GeneratedQuestions q ON e.QuestionId = q.Id
                    WHERE e.ExamId = @ExamId
                    ORDER BY e.CreatedOn DESC";

                var results = await _sqlDb.QueryAsync<dynamic>(query, new { ExamId = examId });

                if (!results.Any())
                {
                    return new NotFoundObjectResult(new
                    {
                        error = $"No evaluations found for exam {examId}"
                    });
                }

                // Calculate aggregate stats
                var totalScore = results.Sum(r => (double)r.Score);
                var totalMarks = results.Sum(r => (int)r.MaxMarks);
                var percentage = totalMarks > 0 ? Math.Round((totalScore / totalMarks) * 100, 2) : 0;

                return new OkObjectResult(new
                {
                    success = true,
                    examId,
                    totalQuestions = results.Count(),
                    totalScore,
                    totalMarks,
                    percentage,
                    evaluations = results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching evaluations: {ex.Message}");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        /// <summary>
        /// Get evaluation for specific question in exam
        /// GET /evaluations/exam/{examId}/question/{questionId}
        /// </summary>
        [Function("GetEvaluationByQuestion")]
        public async Task<IActionResult> GetByQuestion(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evaluations/exam/{examId}/question/{questionId}")] HttpRequest req,
            int examId,
            int questionId)
        {
            _logger.LogInformation($"Fetching evaluation: ExamId={examId}, QuestionId={questionId}");

            try
            {
                var query = @"
                    SELECT 
                        e.Id,
                        e.ExamId,
                        e.QuestionId,
                        q.Question AS QuestionText,
                        e.StudentAnswer,
                        e.ExtractedText,
                        e.IdealAnswer,
                        e.Score,
                        e.MaxMarks,
                        e.Feedback,
                        e.Strengths,
                        e.ImprovementSuggestions,
                        e.KeywordsMatched,
                        e.MissingKeywords,
                        e.ImageBlobPath,
                        e.CreatedOn
                    FROM EvaluatedAnswers e
                    INNER JOIN GeneratedQuestions q ON e.QuestionId = q.Id
                    WHERE e.ExamId = @ExamId AND e.QuestionId = @QuestionId
                    ORDER BY e.CreatedOn DESC";

                var result = await _sqlDb.QuerySingleAsync<dynamic>(
                    query,
                    new { ExamId = examId, QuestionId = questionId }
                );

                if (result == null)
                {
                    return new NotFoundObjectResult(new
                    {
                        error = $"No evaluation found for ExamId={examId}, QuestionId={questionId}"
                    });
                }

                return new OkObjectResult(new
                {
                    success = true,
                    evaluation = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching evaluation: {ex.Message}");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        /// <summary>
        /// Get evaluation by ID
        /// GET /evaluations/{id}
        /// </summary>
        [Function("GetEvaluationById")]
        public async Task<IActionResult> GetById(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evaluations/{id}")] HttpRequest req,
            int id)
        {
            _logger.LogInformation($"Fetching evaluation by ID: {id}");

            try
            {
                var query = @"
                    SELECT 
                        e.*,
                        q.Question AS QuestionText
                    FROM EvaluatedAnswers e
                    INNER JOIN GeneratedQuestions q ON e.QuestionId = q.Id
                    WHERE e.Id = @Id";

                var result = await _sqlDb.QuerySingleAsync<dynamic>(query, new { Id = id });

                if (result == null)
                {
                    return new NotFoundObjectResult(new { error = $"Evaluation {id} not found" });
                }

                return new OkObjectResult(new
                {
                    success = true,
                    evaluation = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error fetching evaluation: {ex.Message}");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }
    }
}
