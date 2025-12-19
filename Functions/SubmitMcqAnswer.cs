using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;
using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// Read MCQ answers from WrittenSubmissions table (stored by backend API)
    /// and map to evaluation-result blob
    /// </summary>
    public class SubmitMcqAnswer
    {
        private readonly ILogger<SubmitMcqAnswer> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _connectionString;

        public SubmitMcqAnswer(
            ILogger<SubmitMcqAnswer> logger,
            BlobServiceClient blobServiceClient)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
            
            _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                ?? throw new InvalidOperationException("SQL connection string not configured");
        }

        [Function("SubmitMcqAnswer")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "mcq/map-to-blob")] HttpRequest req,
            CancellationToken ct)
        {
            _logger.LogInformation("Mapping MCQ submission to blob");

            try
            {
                // Parse request: { "submissionId": "guid" }
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var request = JsonSerializer.Deserialize<McqBlobMappingRequest>(requestBody, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || request.SubmissionId == Guid.Empty)
                {
                    return new BadRequestObjectResult(new { error = "SubmissionId is required" });
                }

                // 1. Fetch submission with MCQ data from WrittenSubmissions table
                var submission = await GetSubmissionWithMcqDataAsync(request.SubmissionId, ct);
                
                if (submission == null)
                {
                    return new NotFoundObjectResult(new { error = $"Submission {request.SubmissionId} not found" });
                }

                // 2. Parse MCQ answers from ExtractedTextJson (stored by backend API)
                var evaluationResult = ParseMcqDataToEvaluationResult(submission);

                // 3. Save evaluation result to blob
                var blobPath = await SaveEvaluationToBlobAsync(
                    submission.Id, 
                    submission.ExamId, 
                    evaluationResult, 
                    ct);

                // 4. Update WrittenSubmissions with blob path
                await UpdateSubmissionBlobPathAsync(submission.Id, blobPath, ct);

                _logger.LogInformation(
                    "MCQ mapped to blob: SubmissionId={SubmissionId}, Score={Score}/{Max}",
                    submission.Id, evaluationResult.TotalScore, evaluationResult.MaxPossibleScore);

                return new OkObjectResult(new
                {
                    success = true,
                    submissionId = submission.Id,
                    examId = submission.ExamId,
                    studentId = submission.StudentId,
                    totalScore = evaluationResult.TotalScore,
                    maxPossibleScore = evaluationResult.MaxPossibleScore,
                    percentage = evaluationResult.Percentage,
                    grade = evaluationResult.Grade,
                    evaluationResultBlobPath = blobPath,
                    downloadUrl = GenerateBlobUrl(blobPath)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error mapping MCQ to blob");
                return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
            }
        }

        private async Task<WrittenSubmission?> GetSubmissionWithMcqDataAsync(
            Guid submissionId, 
            CancellationToken ct)
        {
            const string sql = @"
                SELECT Id, ExamId, StudentId, McqAnswers, McqScore, McqTotalMarks,
                       Status, SubmittedAt, EvaluatedAt
                FROM WrittenSubmissions
                WHERE Id = @SubmissionId";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SubmissionId", submissionId);

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                return new WrittenSubmission
                {
                    Id = reader.GetGuid(0),
                    ExamId = reader.GetString(1),
                    StudentId = reader.GetString(2),
                    ExtractedTextJson = reader.IsDBNull(3) ? null : reader.GetString(3), // McqAnswers
                    TotalScore = reader.IsDBNull(4) ? null : reader.GetDecimal(4), // McqScore
                    MaxPossibleScore = reader.IsDBNull(5) ? null : reader.GetDecimal(5), // McqTotalMarks
                    Status = (WrittenSubmissionStatus)reader.GetInt32(6),
                    SubmittedAt = reader.GetDateTime(7),
                    EvaluatedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
                };
            }

            return null;
        }

        private WrittenEvaluationResult ParseMcqDataToEvaluationResult(WrittenSubmission submission)
        {
            // ExtractedTextJson contains MCQ data from backend API in format:
            // { "questionEvaluations": [...] }
            
            var result = new WrittenEvaluationResult
            {
                WrittenSubmissionId = submission.Id,
                ExamId = submission.ExamId,
                StudentId = submission.StudentId,
                TotalScore = submission.TotalScore ?? 0,
                MaxPossibleScore = submission.MaxPossibleScore ?? 0,
                Percentage = submission.Percentage ?? 0,
                Grade = submission.Grade ?? "",
                EvaluatedAt = submission.EvaluatedAt ?? DateTime.UtcNow,
                QuestionEvaluations = new System.Collections.Generic.List<WrittenQuestionEvaluation>()
            };

            if (!string.IsNullOrEmpty(submission.ExtractedTextJson))
            {
                try
                {
                    var mcqData = JsonDocument.Parse(submission.ExtractedTextJson);
                    
                    if (mcqData.RootElement.TryGetProperty("questionEvaluations", out var questionsElement))
                    {
                        foreach (var q in questionsElement.EnumerateArray())
                        {
                            var extractedAnswer = q.TryGetProperty("extractedAnswer", out var ea) ? ea.GetString() ?? "" : "";
                            var modelAnswer = q.TryGetProperty("modelAnswer", out var ma) ? ma.GetString() ?? "" : "";
                            var awardedScore = q.TryGetProperty("awardedScore", out var aw) ? aw.GetDecimal() : 0;
                            var maxScore = q.TryGetProperty("maxScore", out var ms) ? ms.GetDecimal() : 0;
                            var isCorrect = awardedScore == maxScore;
                            
                            result.QuestionEvaluations.Add(new WrittenQuestionEvaluation
                            {
                                Id = Guid.NewGuid(),
                                WrittenSubmissionId = submission.Id,
                                QuestionId = q.TryGetProperty("questionId", out var qid) ? qid.GetString() ?? "" : "",
                                QuestionNumber = q.TryGetProperty("questionNumber", out var qn) ? qn.GetInt32() : 0,
                                QuestionText = q.TryGetProperty("questionText", out var qt) ? qt.GetString() ?? "" : "",
                                ExtractedAnswer = extractedAnswer,
                                ModelAnswer = modelAnswer,
                                MaxScore = maxScore,
                                AwardedScore = awardedScore,
                                Feedback = isCorrect ? "Correct!" : $"Incorrect. Correct answer is: {modelAnswer}",
                                RubricBreakdown = "",
                                EvaluatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse MCQ data from ExtractedTextJson");
                }
            }

            return result;
        }

        private async Task UpdateSubmissionBlobPathAsync(
            Guid submissionId,
            string blobPath,
            CancellationToken ct)
        {
            const string sql = @"
                UPDATE WrittenSubmissions
                SET EvaluationResultBlobPath = @BlobPath,
                    Status = 3
                WHERE Id = @SubmissionId";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@SubmissionId", submissionId);
            command.Parameters.AddWithValue("@BlobPath", blobPath);

            await command.ExecuteNonQueryAsync(ct);
        }

        private async Task<string> SaveEvaluationToBlobAsync(
            Guid submissionId,
            string examId,
            WrittenEvaluationResult result,
            CancellationToken ct)
        {
            var containerName = "evaluation-results";
            var blobPath = $"{examId}/{submissionId}/evaluation-result.json";

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

            var blobClient = containerClient.GetBlobClient(blobPath);

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var json = JsonSerializer.Serialize(result, jsonOptions);

            using var stream = new System.IO.MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(json));
            
            await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: ct);

            return $"{containerName}/{blobPath}";
        }

        private string GenerateBlobUrl(string blobPath)
        {
            var storageAccountName = Environment.GetEnvironmentVariable("AZURE_STORAGE_ACCOUNT_NAME") 
                ?? "stsmartstudydev";
            return $"https://{storageAccountName}.blob.core.windows.net/{blobPath}";
        }

        private string CalculateGrade(decimal percentage)
        {
            return percentage switch
            {
                >= 90 => "A+",
                >= 80 => "A",
                >= 70 => "B",
                >= 60 => "C",
                >= 50 => "D",
                >= 40 => "E",
                _ => "F"
            };
        }
    }

    // Request Models
    public class McqBlobMappingRequest
    {
        public Guid SubmissionId { get; set; }
    }
}
