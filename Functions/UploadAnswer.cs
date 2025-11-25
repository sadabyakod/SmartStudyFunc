using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services;
using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    public class UploadAnswer
    {
        private readonly ILogger<UploadAnswer> _logger;
        private readonly OcrService _ocrService;
        private readonly BlobServiceClient _blobServiceClient;

        public UploadAnswer(
            ILogger<UploadAnswer> logger,
            OcrService ocrService,
            BlobServiceClient blobServiceClient)
        {
            _logger = logger;
            _ocrService = ocrService;
            _blobServiceClient = blobServiceClient;
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

                if (!form.TryGetValue("questionId", out var questionIdValue) || string.IsNullOrEmpty(questionIdValue))
                {
                    return new BadRequestObjectResult(new { Error = "questionId is required" });
                }

                if (!int.TryParse(examIdValue, out var examId))
                {
                    return new BadRequestObjectResult(new { Error = "examId must be a valid integer" });
                }

                if (!int.TryParse(questionIdValue, out var questionId))
                {
                    return new BadRequestObjectResult(new { Error = "questionId must be a valid integer" });
                }

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

                _logger.LogInformation("Processing upload: ExamId={ExamId}, QuestionId={QuestionId}, File={FileName} ({Size} bytes)",
                    examId, questionId, file.FileName, file.Length);

                // Upload to blob storage
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var blobName = $"answers/{examId}/{questionId}/{timestamp}{extension}";

                var containerClient = _blobServiceClient.GetBlobContainerClient("student-answers");
                await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

                var blobClient = containerClient.GetBlobClient(blobName);

                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, overwrite: true, cancellationToken: ct);
                }

                _logger.LogInformation("File uploaded to blob: {BlobName}", blobName);

                // Extract text using OCR
                string extractedText;
                using (var stream = file.OpenReadStream())
                {
                    extractedText = await _ocrService.ExtractTextFromStreamAsync(stream, file.FileName, ct);
                }

                _logger.LogInformation("OCR extraction complete. Extracted {Length} characters", extractedText.Length);

                // Return response
                var response = new UploadAnswerResponse
                {
                    Success = true,
                    BlobPath = blobName,
                    ExtractedText = extractedText,
                    FileName = file.FileName,
                    FileSize = file.Length,
                    ExamId = examId,
                    QuestionId = questionId
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
    }
}
