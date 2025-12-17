using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// API endpoint to check the status of a written submission.
    /// Mobile apps can poll this endpoint to track processing progress.
    /// </summary>
    public class GetSubmissionStatus
    {
        private readonly ILogger<GetSubmissionStatus> _logger;
        private readonly string _connectionString;

        public GetSubmissionStatus(ILogger<GetSubmissionStatus> logger)
        {
            _logger = logger;
            _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("AzureSqlConnectionString")
                ?? "";
        }

        [Function("GetSubmissionStatus")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "submissions/{submissionId}")] HttpRequest req,
            string submissionId,
            CancellationToken ct)
        {
            _logger.LogInformation("GetSubmissionStatus: {SubmissionId}", submissionId);

            try
            {
                if (!Guid.TryParse(submissionId, out var id))
                {
                    return new BadRequestObjectResult(new { error = "Invalid submissionId format. Must be a valid GUID." });
                }

                if (string.IsNullOrEmpty(_connectionString))
                {
                    return new ObjectResult(new { error = "Database not configured" }) { StatusCode = 503 };
                }

                var submission = await GetSubmissionAsync(id, ct);

                if (submission == null)
                {
                    return new NotFoundObjectResult(new { error = "Submission not found", submissionId = submissionId });
                }

                return new OkObjectResult(submission);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting submission status: {SubmissionId}", submissionId);
                return new ObjectResult(new { error = "Failed to get submission status", details = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        private async Task<object?> GetSubmissionAsync(Guid id, CancellationToken ct)
        {
            const string sql = @"
                SELECT 
                    Id,
                    ExamId,
                    StudentId,
                    Status,
                    TotalScore,
                    MaxPossibleScore,
                    Percentage,
                    Grade,
                    ErrorMessage,
                    SubmittedAt,
                    OcrStartedAt,
                    OcrCompletedAt,
                    EvaluationStartedAt,
                    EvaluatedAt,
                    RetryCount
                FROM WrittenSubmissions
                WHERE Id = @Id";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                var status = reader.GetInt32(3);
                var statusText = status switch
                {
                    0 => "Uploaded",
                    1 => "OCR Processing",
                    2 => "Evaluating",
                    3 => "Completed",
                    4 => "Failed",
                    _ => "Unknown"
                };

                return new
                {
                    submissionId = reader.GetGuid(0).ToString(),
                    examId = reader.GetString(1),
                    studentId = reader.GetString(2),
                    status = statusText,
                    statusCode = status,
                    totalScore = reader.IsDBNull(4) ? null : (decimal?)reader.GetDecimal(4),
                    maxPossibleScore = reader.IsDBNull(5) ? null : (decimal?)reader.GetDecimal(5),
                    percentage = reader.IsDBNull(6) ? null : (decimal?)reader.GetDecimal(6),
                    grade = reader.IsDBNull(7) ? null : reader.GetString(7),
                    errorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
                    submittedAt = reader.IsDBNull(9) ? null : (DateTime?)reader.GetDateTime(9),
                    ocrStartedAt = reader.IsDBNull(10) ? null : (DateTime?)reader.GetDateTime(10),
                    ocrCompletedAt = reader.IsDBNull(11) ? null : (DateTime?)reader.GetDateTime(11),
                    evaluationStartedAt = reader.IsDBNull(12) ? null : (DateTime?)reader.GetDateTime(12),
                    evaluatedAt = reader.IsDBNull(13) ? null : (DateTime?)reader.GetDateTime(13),
                    retryCount = reader.GetInt32(14),
                    isComplete = status == 3,
                    isFailed = status == 4,
                    isProcessing = status >= 0 && status <= 2
                };
            }

            return null;
        }
    }
}
