using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Dapper;
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
        private readonly string _connectionString;

        public GetEvaluationResults(ILogger<GetEvaluationResults> logger)
        {
            _logger = logger;
            _connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? throw new InvalidOperationException("SQL connection string not configured");
        }

        /// <summary>
        /// Get all evaluations for an exam
        /// GET /evaluations/exam/{examId}
        /// </summary>
        [Function("GetEvaluationsByExam")]
        public async Task<IActionResult> GetByExam(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evaluations/exam/{examId}")] HttpRequest req,
            string examId)
        {
            _logger.LogInformation("Fetching evaluations for exam: {ExamId}", examId);

            try
            {
                // Query evaluations through WrittenSubmissions (which has ExamId)
                var query = @"
                    SELECT 
                        e.Id,
                        ws.ExamId,
                        e.QuestionId,
                        e.QuestionNumber,
                        e.ExtractedAnswer as StudentAnswer,
                        e.ModelAnswer,
                        e.AwardedScore as Score,
                        e.MaxScore as MaxMarks,
                        e.Feedback,
                        e.RubricBreakdown,
                        e.EvaluatedAt as CreatedOn,
                        ws.StudentId,
                        ws.Status as SubmissionStatus,
                        ws.TotalScore as SubmissionTotalScore,
                        ws.MaxPossibleScore as SubmissionMaxScore,
                        ws.Percentage as SubmissionPercentage
                    FROM WrittenQuestionEvaluations e
                    INNER JOIN WrittenSubmissions ws ON e.WrittenSubmissionId = ws.Id
                    WHERE ws.ExamId = @ExamId
                    ORDER BY e.QuestionNumber, e.EvaluatedAt DESC";

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                var results = (await conn.QueryAsync<dynamic>(query, new { ExamId = examId })).ToList();

                if (!results.Any())
                {
                    return new NotFoundObjectResult(new
                    {
                        error = $"No evaluations found for exam {examId}"
                    });
                }

                // Calculate aggregate stats
                var totalScore = results.Sum(r => (double)(decimal)r.Score);
                var totalMarks = results.Sum(r => (double)(decimal)r.MaxMarks);
                var percentage = totalMarks > 0 ? Math.Round((totalScore / totalMarks) * 100, 2) : 0;
                
                // Get submission status (from first result since all belong to same exam)
                var submissionStatus = (int)results[0].SubmissionStatus;
                var statusText = submissionStatus switch
                {
                    0 => "PendingEvaluation",
                    1 => "OcrProcessing",
                    2 => "Evaluating",
                    3 => "Completed",
                    4 => "Failed",
                    _ => "Unknown"
                };

                return new OkObjectResult(new
                {
                    success = true,
                    examId,
                    status = statusText,
                    statusCode = submissionStatus,
                    totalQuestions = results.Count,
                    totalScore,
                    totalMarks,
                    percentage,
                    evaluations = results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching evaluations for exam {ExamId}", examId);
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
            string examId,
            string questionId)
        {
            _logger.LogInformation("Fetching evaluation: ExamId={ExamId}, QuestionId={QuestionId}", examId, questionId);

            try
            {
                var query = @"
                    SELECT 
                        e.Id,
                        q.ExamId,
                        e.QuestionId,
                        q.QuestionText,
                        e.ExtractedAnswer as StudentAnswer,
                        e.ModelAnswer as IdealAnswer,
                        e.AwardedScore as Score,
                        e.MaxScore as MaxMarks,
                        e.Feedback,
                        e.RubricBreakdown,
                        e.EvaluatedAt as CreatedOn
                    FROM WrittenQuestionEvaluations e
                    INNER JOIN ExamQuestions q ON e.QuestionId = q.Id
                    WHERE q.ExamId = @ExamId AND e.QuestionId = @QuestionId
                    ORDER BY e.EvaluatedAt DESC";

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    query,
                    new { ExamId = examId, QuestionId = Guid.Parse(questionId) }
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
                _logger.LogError(ex, "Error fetching evaluation: {Message}", ex.Message);
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
            string id)
        {
            _logger.LogInformation("Fetching evaluation by ID: {Id}", id);

            try
            {
                var query = @"
                    SELECT 
                        e.Id,
                        q.ExamId,
                        e.QuestionId,
                        q.QuestionText,
                        e.ExtractedAnswer as StudentAnswer,
                        e.ModelAnswer as IdealAnswer,
                        e.AwardedScore as Score,
                        e.MaxScore as MaxMarks,
                        e.Feedback,
                        e.RubricBreakdown,
                        e.EvaluatedAt as CreatedOn
                    FROM WrittenQuestionEvaluations e
                    INNER JOIN ExamQuestions q ON e.QuestionId = q.Id
                    WHERE e.Id = @Id";

                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(query, new { Id = Guid.Parse(id) });

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
                _logger.LogError(ex, "Error fetching evaluation by ID: {Message}", ex.Message);
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }
    }
}
