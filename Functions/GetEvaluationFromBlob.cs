using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// API endpoint to retrieve full evaluation results from blob storage.
    /// This is the primary endpoint for mobile apps to fetch detailed evaluation results.
    /// 
    /// Flow:
    /// 1. Mobile app calls GET /submissions/{id} to check status
    /// 2. When status = "Completed", call GET /submissions/{id}/result to get full evaluation
    /// </summary>
    public class GetEvaluationFromBlob
    {
        private readonly ILogger<GetEvaluationFromBlob> _logger;
        private readonly string _connectionString;
        private readonly string _storageConnectionString;

        public GetEvaluationFromBlob(ILogger<GetEvaluationFromBlob> logger)
        {
            _logger = logger;
            _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("AzureSqlConnectionString")
                ?? "";
            _storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
                ?? "";
        }

        /// <summary>
        /// Get full evaluation result from blob storage
        /// GET /submissions/{submissionId}/result
        /// 
        /// Returns the complete evaluation JSON including:
        /// - Total score and percentage
        /// - Per-question evaluations with rubric breakdowns
        /// - Student answers, model answers, and feedback
        /// </summary>
        [Function("GetEvaluationFromBlob")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "submissions/{submissionId}/result")] HttpRequest req,
            string submissionId,
            CancellationToken ct)
        {
            _logger.LogInformation("GetEvaluationFromBlob: {SubmissionId}", submissionId);

            try
            {
                if (!Guid.TryParse(submissionId, out var id))
                {
                    return new BadRequestObjectResult(new { 
                        success = false,
                        error = "Invalid submissionId format. Must be a valid GUID." 
                    });
                }

                if (string.IsNullOrEmpty(_connectionString))
                {
                    return new ObjectResult(new { 
                        success = false,
                        error = "Database not configured" 
                    }) { StatusCode = 503 };
                }

                if (string.IsNullOrEmpty(_storageConnectionString))
                {
                    return new ObjectResult(new { 
                        success = false,
                        error = "Storage not configured" 
                    }) { StatusCode = 503 };
                }

                // Get blob path from database
                var (blobPath, status, errorMessage) = await GetBlobPathAsync(id, ct);

                if (blobPath == null && status == -1)
                {
                    return new NotFoundObjectResult(new { 
                        success = false,
                        error = "Submission not found", 
                        submissionId = submissionId 
                    });
                }

                if (status != 3) // Not Completed
                {
                    var statusText = status switch
                    {
                        0 => "Uploaded",
                        1 => "OCR Processing",
                        2 => "Evaluating",
                        4 => "Failed",
                        _ => "Unknown"
                    };

                    return new ObjectResult(new { 
                        success = false,
                        error = $"Evaluation not ready. Current status: {statusText}",
                        status = statusText,
                        statusCode = status,
                        errorMessage = status == 4 ? errorMessage : null
                    }) { StatusCode = status == 4 ? 400 : 202 }; // 202 Accepted = still processing
                }

                if (string.IsNullOrEmpty(blobPath))
                {
                    return new ObjectResult(new { 
                        success = false,
                        error = "Evaluation completed but result file not found",
                        submissionId = submissionId
                    }) { StatusCode = 500 };
                }

                // Read from blob storage
                var evaluationJson = await ReadBlobAsync(blobPath, ct);

                if (evaluationJson == null)
                {
                    return new NotFoundObjectResult(new { 
                        success = false,
                        error = "Evaluation result blob not found",
                        blobPath = blobPath
                    });
                }

                // Parse and return as JSON
                try
                {
                    var evaluationResult = JsonSerializer.Deserialize<JsonElement>(evaluationJson);
                    return new OkObjectResult(new {
                        success = true,
                        submissionId = submissionId,
                        result = evaluationResult
                    });
                }
                catch (JsonException)
                {
                    // Return as raw string if not valid JSON
                    return new OkObjectResult(new {
                        success = true,
                        submissionId = submissionId,
                        rawResult = evaluationJson
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting evaluation from blob: {SubmissionId}", submissionId);
                return new ObjectResult(new { 
                    success = false,
                    error = "Failed to get evaluation result", 
                    details = ex.Message 
                }) { StatusCode = 500 };
            }
        }

        private async Task<(string? blobPath, int status, string? errorMessage)> GetBlobPathAsync(Guid id, CancellationToken ct)
        {
            const string sql = @"
                SELECT EvaluationResultBlobPath, Status, ErrorMessage
                FROM WrittenSubmissions
                WHERE Id = @Id";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                var blobPath = reader.IsDBNull(0) ? null : reader.GetString(0);
                var status = reader.GetInt32(1);
                var errorMessage = reader.IsDBNull(2) ? null : reader.GetString(2);
                return (blobPath, status, errorMessage);
            }

            return (null, -1, null); // Not found
        }

        private async Task<string?> ReadBlobAsync(string blobPath, CancellationToken ct)
        {
            try
            {
                // blobPath format: "evaluation-results/{examId}/{submissionId}/evaluation-result.json"
                // OR could be full path like "https://storage.blob.core.windows.net/container/path"
                
                string containerName;
                string blobName;

                if (blobPath.StartsWith("http"))
                {
                    // Parse URL
                    var uri = new Uri(blobPath);
                    var pathParts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
                    containerName = pathParts[0];
                    blobName = pathParts.Length > 1 ? pathParts[1] : "";
                }
                else
                {
                    // Parse path format: "container/blob/path"
                    var firstSlash = blobPath.IndexOf('/');
                    if (firstSlash > 0)
                    {
                        containerName = blobPath.Substring(0, firstSlash);
                        blobName = blobPath.Substring(firstSlash + 1);
                    }
                    else
                    {
                        containerName = "evaluation-results";
                        blobName = blobPath;
                    }
                }

                _logger.LogInformation("Reading blob: Container={Container}, Blob={Blob}", containerName, blobName);

                var blobServiceClient = new BlobServiceClient(_storageConnectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                if (!await blobClient.ExistsAsync(ct))
                {
                    _logger.LogWarning("Blob not found: {BlobPath}", blobPath);
                    return null;
                }

                var response = await blobClient.DownloadContentAsync(ct);
                return response.Value.Content.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading blob: {BlobPath}", blobPath);
                throw;
            }
        }
    }
}
