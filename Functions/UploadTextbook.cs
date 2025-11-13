using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// HTTP-triggered Azure Function for uploading textbook PDF files.
    /// Accepts form-data with className, subject, chapter, and file.
    /// Uploads to blob storage with folder structure and metadata.
    /// ProcessBlobFile will automatically handle PDF processing via BlobTrigger.
    /// </summary>
    public class UploadTextbook
    {
        private readonly ILogger<UploadTextbook> _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public UploadTextbook(ILogger<UploadTextbook> logger, BlobServiceClient blobServiceClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
        }

        [Function("UploadTextbook")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "upload/textbook")] HttpRequestData req)
        {
            _logger.LogInformation("UploadTextbook function triggered");

            try
            {
                // Parse multipart form data
                var formData = await req.ReadFormDataAsync();

                // Extract metadata fields (support both "className" and "Class")
                var className = formData["className"] ?? formData["Class"];
                var subject = formData["subject"];
                var chapter = formData["chapter"];

                // Validate required fields
                if (string.IsNullOrWhiteSpace(className) || 
                    string.IsNullOrWhiteSpace(subject) || 
                    string.IsNullOrWhiteSpace(chapter))
                {
                    _logger.LogWarning("Missing required metadata fields");
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new
                    {
                        error = "Missing required fields",
                        message = "className, subject, and chapter are required"
                    });
                    return badRequest;
                }

                // Get uploaded file
                var file = formData.Files.Count > 0 ? formData.Files[0] : null;
                if (file == null || file.Length == 0)
                {
                    _logger.LogWarning("No file uploaded");
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new
                    {
                        error = "No file uploaded",
                        message = "A PDF file is required"
                    });
                    return badRequest;
                }

                // Validate file extension
                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (fileExtension != ".pdf")
                {
                    _logger.LogWarning("Invalid file type: {Extension}", fileExtension);
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new
                    {
                        error = "Invalid file type",
                        message = "Only PDF files are allowed"
                    });
                    return badRequest;
                }

                // Sanitize folder/file names (remove invalid characters)
                var sanitizedClassName = SanitizeBlobName(className);
                var sanitizedSubject = SanitizeBlobName(subject);
                var sanitizedChapter = SanitizeBlobName(chapter);
                var sanitizedFileName = SanitizeBlobName(file.FileName);

                // Construct blob path: textbooks/{className}/{subject}/{chapter}/{fileName}
                var blobPath = $"{sanitizedClassName}/{sanitizedSubject}/{sanitizedChapter}/{sanitizedFileName}";

                _logger.LogInformation("Uploading file to blob path: textbooks/{BlobPath}", blobPath);

                // Get blob container client
                var containerClient = _blobServiceClient.GetBlobContainerClient("textbooks");
                await containerClient.CreateIfNotExistsAsync();

                // Get blob client
                var blobClient = containerClient.GetBlobClient(blobPath);

                // Upload file with metadata
                using var fileStream = file.OpenReadStream();
                
                var metadata = new Dictionary<string, string>
                {
                    { "className", className },
                    { "subject", subject },
                    { "chapter", chapter },
                    { "originalFileName", file.FileName },
                    { "uploadedOn", DateTime.UtcNow.ToString("o") }
                };

                await blobClient.UploadAsync(fileStream, overwrite: true);
                await blobClient.SetMetadataAsync(metadata);

                _logger.LogInformation(
                    "Successfully uploaded: {FileName} | Class: {ClassName} | Subject: {Subject} | Chapter: {Chapter}",
                    file.FileName, className, subject, chapter);

                // Return success response
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "File uploaded successfully",
                    data = new
                    {
                        fileName = file.FileName,
                        blobPath = $"textbooks/{blobPath}",
                        className = className,
                        subject = subject,
                        chapter = chapter,
                        fileSize = file.Length,
                        uploadedAt = DateTime.UtcNow
                    }
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading textbook file");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new
                {
                    error = "Upload failed",
                    message = ex.Message
                });
                return errorResponse;
            }
        }

        /// <summary>
        /// Sanitizes blob names by removing or replacing invalid characters.
        /// Azure blob names cannot contain: \ / : * ? " &lt; &gt; |
        /// </summary>
        private static string SanitizeBlobName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "unknown";

            // Replace invalid characters with underscore
            char[] invalidChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            string sanitized = name;
            
            foreach (var c in invalidChars)
            {
                sanitized = sanitized.Replace(c, '_');
            }

            // Replace spaces with hyphens for better URLs
            sanitized = sanitized.Replace(' ', '-');

            return sanitized.Trim();
        }
    }

    /// <summary>
    /// Extension methods for reading multipart form data from HTTP requests.
    /// </summary>
    public static class HttpRequestDataExtensions
    {
        public static async Task<FormData> ReadFormDataAsync(this HttpRequestData req)
        {
            var formData = new FormData();
            var contentType = req.Headers.GetValues("Content-Type").FirstOrDefault();

            if (contentType == null || !contentType.Contains("multipart/form-data"))
            {
                return formData;
            }

            // Extract boundary from Content-Type header
            var boundary = contentType.Split(';')
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.StartsWith("boundary="))
                ?.Substring("boundary=".Length)
                .Trim('"');

            if (string.IsNullOrWhiteSpace(boundary))
            {
                return formData;
            }

            using var reader = new StreamReader(req.Body);
            var content = await reader.ReadToEndAsync();

            // Simple multipart parser - splits by boundary
            var parts = content.Split(new[] { $"--{boundary}" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                if (part.Trim() == "--") continue;

                var lines = part.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                var headerEndIndex = Array.FindIndex(lines, string.IsNullOrWhiteSpace);

                if (headerEndIndex < 0) continue;

                // Parse Content-Disposition header
                var dispositionLine = lines.FirstOrDefault(l => l.StartsWith("Content-Disposition:"));
                if (dispositionLine == null) continue;

                var nameMatch = System.Text.RegularExpressions.Regex.Match(dispositionLine, @"name=""([^""]+)""");
                var filenameMatch = System.Text.RegularExpressions.Regex.Match(dispositionLine, @"filename=""([^""]+)""");

                if (!nameMatch.Success) continue;

                var fieldName = nameMatch.Groups[1].Value;
                var dataStartIndex = headerEndIndex + 1;
                var dataLines = lines.Skip(dataStartIndex).ToArray();
                var data = string.Join("\n", dataLines).TrimEnd('\r', '\n', '-');

                if (filenameMatch.Success)
                {
                    // This is a file
                    var fileName = filenameMatch.Groups[1].Value;
                    var fileBytes = System.Text.Encoding.Latin1.GetBytes(data);
                    formData.Files.Add(new FormFile
                    {
                        FileName = fileName,
                        Name = fieldName,
                        Content = fileBytes,
                        Length = fileBytes.Length
                    });
                }
                else
                {
                    // This is a regular field
                    formData[fieldName] = data.Trim();
                }
            }

            return formData;
        }
    }

    /// <summary>
    /// Represents parsed multipart form data.
    /// </summary>
    public class FormData
    {
        private readonly Dictionary<string, string> _fields = new();
        public List<FormFile> Files { get; } = new();

        public string this[string key]
        {
            get => _fields.TryGetValue(key, out var value) ? value : string.Empty;
            set => _fields[key] = value;
        }
    }

    /// <summary>
    /// Represents an uploaded file from multipart form data.
    /// </summary>
    public class FormFile
    {
        public string FileName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public long Length { get; set; }

        public Stream OpenReadStream() => new MemoryStream(Content);
    }
}
