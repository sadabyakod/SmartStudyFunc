using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    public class UploadAnswer
    {
        private readonly ILogger<UploadAnswer> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly string _connectionString;

        private const string QueueName = "written-submission-processing";

        public UploadAnswer(
            ILogger<UploadAnswer> logger,
            BlobServiceClient blobServiceClient,
            QueueServiceClient queueServiceClient)
        {
            _logger = logger;
            _blobServiceClient = blobServiceClient;
            _queueServiceClient = queueServiceClient;
            
            // Get connection string for database
            _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("AzureSqlConnectionString")
                ?? "";
        }

        [Function("UploadAnswer")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "answers/upload")] HttpRequest req,
            CancellationToken ct)
        {
            _logger.LogInformation("UploadAnswer function triggered");

            try
            {
                // Validate form data
                if (!req.HasFormContentType)
                {
                    return new BadRequestObjectResult(new { Error = "Content-Type must be multipart/form-data" });
                }

                var form = await req.ReadFormAsync(ct);

                // Extract form fields
                if (!form.TryGetValue("examId", out var examIdValue) || string.IsNullOrEmpty(examIdValue))
                {
                    return new BadRequestObjectResult(new { Error = "examId is required" });
                }

                // Get optional studentId (default to "anonymous")
                form.TryGetValue("studentId", out var studentIdValue);
                var studentId = string.IsNullOrEmpty(studentIdValue) ? "anonymous" : studentIdValue.ToString();

                // examId can be string (no need to parse as int)
                var examId = examIdValue.ToString();

                // Extract uploaded file
                if (form.Files.Count == 0)
                {
                    return new BadRequestObjectResult(new { Error = "No file uploaded" });
                }

                var file = form.Files[0];

                if (file.Length == 0)
                {
                    return new BadRequestObjectResult(new { Error = "Uploaded file is empty" });
                }

                // Validate file type
                var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (Array.IndexOf(allowedExtensions, extension) == -1)
                {
                    return new BadRequestObjectResult(new
                    {
                        Error = $"Invalid file type '{extension}'. Allowed: PDF, JPG, JPEG, PNG"
                    });
                }

                // Validate file size (max 10MB)
                if (file.Length > 10 * 1024 * 1024)
                {
                    return new BadRequestObjectResult(new { Error = "File size exceeds 10MB limit" });
                }

                _logger.LogInformation("Processing upload: ExamId={ExamId}, StudentId={StudentId}, File={FileName} ({Size} bytes)",
                    examId, studentId, file.FileName, file.Length);

                // Generate submission ID and blob path
                // Format: students-answer-sheets/{examId}/{studentId}/{timestamp}_{guid}.{ext}
                var submissionId = Guid.NewGuid();
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var blobName = $"{examId}/{studentId}/{timestamp}_{submissionId}{extension}";

                // Upload to blob storage - using students-answer-sheets container
                var containerClient = _blobServiceClient.GetBlobContainerClient("students-answer-sheets");
                await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

                var blobClient = containerClient.GetBlobClient(blobName);

                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: ct);
                }

                _logger.LogInformation("File uploaded to blob: {BlobName}", blobName);

                // Create WrittenSubmission record in database
                var filePaths = new List<string> { blobName };
                await CreateWrittenSubmissionAsync(submissionId, examId, studentId, filePaths, ct);
                
                _logger.LogInformation("Created WrittenSubmission record: {SubmissionId}", submissionId);

                // Queue message for background processing (OCR + AI evaluation)
                await QueueProcessingMessageAsync(submissionId, examId, studentId, filePaths, ct);
                
                _logger.LogInformation("Queued processing message for: {SubmissionId}", submissionId);

                // Return response with submissionId for status tracking
                var response = new
                {
                    success = true,
                    submissionId = submissionId.ToString(),
                    status = "Processing",
                    message = "Answer sheet uploaded successfully. Check status using /api/submissions/{submissionId}",
                    blobPath = blobName,
                    fileName = file.FileName,
                    fileSize = file.Length,
                    examId = examId,
                    studentId = studentId
                };

                return new OkObjectResult(response);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Upload operation was cancelled");
                return new StatusCodeResult(499); // Client Closed Request
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload failed");
                return new ObjectResult(new { Error = "Upload processing failed", Details = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Creates a WrittenSubmission record in the database with Status = Uploaded (0)
        /// </summary>
        private async Task CreateWrittenSubmissionAsync(
            Guid submissionId, 
            string examId, 
            string studentId, 
            List<string> filePaths,
            CancellationToken ct)
        {
            const string sql = @"
                INSERT INTO WrittenSubmissions 
                    (Id, ExamId, StudentId, FilePaths, Status, SubmittedAt, RetryCount)
                VALUES 
                    (@Id, @ExamId, @StudentId, @FilePaths, 0, GETUTCDATE(), 0)";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", submissionId);
            command.Parameters.AddWithValue("@ExamId", examId);
            command.Parameters.AddWithValue("@StudentId", studentId);
            command.Parameters.AddWithValue("@FilePaths", JsonSerializer.Serialize(filePaths));

            await command.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Queues a message for background processing (OCR + AI evaluation)
        /// </summary>
        private async Task QueueProcessingMessageAsync(
            Guid submissionId,
            string examId,
            string studentId,
            List<string> filePaths,
            CancellationToken ct)
        {
            var message = new
            {
                WrittenSubmissionId = submissionId,
                ExamId = examId,
                StudentId = studentId,
                FilePaths = filePaths,
                RetryCount = 0,
                QueuedAt = DateTime.UtcNow
            };

            var messageJson = JsonSerializer.Serialize(message);
            var base64Message = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(messageJson));

            var queueClient = _queueServiceClient.GetQueueClient(QueueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: ct);
            await queueClient.SendMessageAsync(base64Message, ct);
        }
    }
}
