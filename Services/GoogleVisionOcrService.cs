using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Service for performing OCR using Google Cloud Vision API.
    /// Supports both API Key and Service Account authentication.
    /// Uses DOCUMENT_TEXT_DETECTION for handwritten text recognition.
    /// </summary>
    public interface IGoogleVisionOcrService
    {
        Task<OcrResult> ExtractTextFromBlobsAsync(
            IEnumerable<string> blobPaths,
            Guid submissionId,
            CancellationToken cancellationToken = default);
    }

    public class OcrResult
    {
        public bool Success { get; set; }
        public string CombinedText { get; set; } = string.Empty;
        public string NormalizedText { get; set; } = string.Empty;
        public List<OcrPageDetail> Pages { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public float AverageConfidence { get; set; }
    }

    public class OcrPageDetail
    {
        public int PageNumber { get; set; }
        public string BlobPath { get; set; } = string.Empty;
        public string RawText { get; set; } = string.Empty;
        public float Confidence { get; set; }
    }

    public class GoogleVisionOcrService : IGoogleVisionOcrService
    {
        private readonly ImageAnnotatorClient? _visionClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<GoogleVisionOcrService> _logger;
        private readonly string? _apiKey;
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(60);
        private const string VisionApiBaseUrl = "https://vision.googleapis.com/v1/images:annotate";

        /// <summary>
        /// Creates a GoogleVisionOcrService with support for both API Key and Service Account auth.
        /// Priority: API Key (GoogleCloud:ApiKey) > Service Account (GOOGLE_APPLICATION_CREDENTIALS)
        /// </summary>
        public GoogleVisionOcrService(
            IConfiguration configuration,
            BlobServiceClient blobServiceClient,
            ILogger<GoogleVisionOcrService> logger,
            HttpClient? httpClient = null)
        {
            _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? new HttpClient();

            // Try API Key first (simpler setup)
            _apiKey = configuration["GoogleCloud:ApiKey"];

            if (!string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogInformation("GoogleVisionOcrService initialized with API Key authentication");
                _visionClient = null; // Will use REST API with API Key
            }
            else
            {
                // Fall back to service account (GOOGLE_APPLICATION_CREDENTIALS)
                _logger.LogInformation("GoogleVisionOcrService initialized with Service Account authentication");
                _visionClient = ImageAnnotatorClient.Create();
            }
        }

        /// <summary>
        /// Alternative constructor for when ImageAnnotatorClient is pre-configured (DI scenario)
        /// </summary>
        public GoogleVisionOcrService(
            ImageAnnotatorClient visionClient,
            BlobServiceClient blobServiceClient,
            ILogger<GoogleVisionOcrService> logger)
        {
            _visionClient = visionClient;
            _blobServiceClient = blobServiceClient;
            _logger = logger;
            _apiKey = null;
            _httpClient = new HttpClient();
        }

        public async Task<OcrResult> ExtractTextFromBlobsAsync(
            IEnumerable<string> blobPaths,
            Guid submissionId,
            CancellationToken cancellationToken = default)
        {
            var result = new OcrResult();
            var pages = new List<OcrPageDetail>();
            var pageNumber = 0;

            _logger.LogInformation(
                "[{SubmissionId}] Starting OCR for {FileCount} files",
                submissionId, blobPaths.Count());

            foreach (var blobPath in blobPaths)
            {
                pageNumber++;
                
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(_timeout);

                    var pageResult = await ProcessSingleBlobAsync(
                        blobPath, pageNumber, submissionId, cts.Token);
                    
                    pages.Add(pageResult);

                    _logger.LogInformation(
                        "[{SubmissionId}] OCR completed for page {PageNumber}/{TotalPages}, confidence: {Confidence:P2}",
                        submissionId, pageNumber, blobPaths.Count(), pageResult.Confidence);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError(
                        "[{SubmissionId}] OCR timeout for page {PageNumber}: {BlobPath}",
                        submissionId, pageNumber, blobPath);
                    
                    result.Success = false;
                    result.ErrorMessage = $"OCR timeout on page {pageNumber}";
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[{SubmissionId}] OCR failed for page {PageNumber}: {BlobPath}",
                        submissionId, pageNumber, blobPath);
                    
                    result.Success = false;
                    result.ErrorMessage = $"OCR failed on page {pageNumber}: {ex.Message}";
                    return result;
                }
            }

            // Combine text in page order
            var combinedText = CombinePageTexts(pages);
            var normalizedText = NormalizeOcrText(combinedText);

            result.Success = true;
            result.Pages = pages;
            result.CombinedText = combinedText;
            result.NormalizedText = normalizedText;
            result.AverageConfidence = pages.Count > 0 
                ? pages.Average(p => p.Confidence) 
                : 0f;

            _logger.LogInformation(
                "[{SubmissionId}] OCR completed successfully. Total characters: {CharCount}, Avg confidence: {Confidence:P2}",
                submissionId, normalizedText.Length, result.AverageConfidence);

            return result;
        }

        private async Task<OcrPageDetail> ProcessSingleBlobAsync(
            string blobPath,
            int pageNumber,
            Guid submissionId,
            CancellationToken cancellationToken)
        {
            // Parse container and blob name from path
            var (containerName, blobName) = ParseBlobPath(blobPath);
            
            // Download blob content
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);
            
            using var memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            var imageBytes = memoryStream.ToArray();

            // Use API Key REST API or SDK based on configuration
            if (!string.IsNullOrEmpty(_apiKey))
            {
                return await ProcessWithApiKeyAsync(imageBytes, blobPath, pageNumber, submissionId, cancellationToken);
            }
            else
            {
                return await ProcessWithSdkAsync(imageBytes, blobPath, pageNumber, submissionId, cancellationToken);
            }
        }

        /// <summary>
        /// Process image using Google Cloud Vision REST API with API Key.
        /// Uses DOCUMENT_TEXT_DETECTION for handwritten text recognition.
        /// </summary>
        private async Task<OcrPageDetail> ProcessWithApiKeyAsync(
            byte[] imageBytes,
            string blobPath,
            int pageNumber,
            Guid submissionId,
            CancellationToken cancellationToken)
        {
            var base64Image = Convert.ToBase64String(imageBytes);
            
            // Build request for DOCUMENT_TEXT_DETECTION (best for handwriting)
            var requestBody = new
            {
                requests = new[]
                {
                    new
                    {
                        image = new { content = base64Image },
                        features = new[]
                        {
                            new { type = "DOCUMENT_TEXT_DETECTION", maxResults = 1 }
                        },
                        imageContext = new
                        {
                            languageHints = new[] { "en", "hi", "kn" } // English, Hindi, Kannada
                        }
                    }
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var requestUrl = $"{VisionApiBaseUrl}?key={_apiKey}";
            var response = await _httpClient.PostAsync(requestUrl, httpContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "[{SubmissionId}] Google Vision API error for page {PageNumber}: {Status} - {Error}",
                    submissionId, pageNumber, response.StatusCode, errorContent);
                throw new HttpRequestException($"Google Vision API returned {response.StatusCode}: {errorContent}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<GoogleVisionApiResponse>(responseJson, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var fullText = apiResponse?.Responses?.FirstOrDefault()?.FullTextAnnotation?.Text ?? string.Empty;
            var confidence = apiResponse?.Responses?.FirstOrDefault()?.FullTextAnnotation?.Pages?
                .SelectMany(p => p.Blocks ?? Enumerable.Empty<GoogleVisionBlock>())
                .SelectMany(b => b.Paragraphs ?? Enumerable.Empty<GoogleVisionParagraph>())
                .Where(p => p.Confidence > 0)
                .Select(p => p.Confidence)
                .DefaultIfEmpty(0.8f)
                .Average() ?? 0.8f;

            _logger.LogDebug(
                "[{SubmissionId}] API Key OCR page {PageNumber}: {CharCount} chars, {Confidence:P2} confidence",
                submissionId, pageNumber, fullText.Length, confidence);

            return new OcrPageDetail
            {
                PageNumber = pageNumber,
                BlobPath = blobPath,
                RawText = fullText,
                Confidence = confidence
            };
        }

        /// <summary>
        /// Process image using Google Cloud Vision SDK (service account auth).
        /// Uses DOCUMENT_TEXT_DETECTION for handwritten text recognition.
        /// </summary>
        private async Task<OcrPageDetail> ProcessWithSdkAsync(
            byte[] imageBytes,
            string blobPath,
            int pageNumber,
            Guid submissionId,
            CancellationToken cancellationToken)
        {
            var image = Image.FromBytes(imageBytes);

            // Check if PDF (multi-page support)
            if (blobPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return await ProcessPdfAsync(image, blobPath, pageNumber, submissionId, cancellationToken);
            }

            // Process single image using DOCUMENT_TEXT_DETECTION
            var response = await _visionClient!.DetectDocumentTextAsync(image);
            
            var text = response?.Text ?? string.Empty;
            var confidence = CalculateAverageConfidence(response);

            return new OcrPageDetail
            {
                PageNumber = pageNumber,
                BlobPath = blobPath,
                RawText = text,
                Confidence = confidence
            };
        }

        private async Task<OcrPageDetail> ProcessPdfAsync(
            Image image,
            string blobPath,
            int pageNumber,
            Guid submissionId,
            CancellationToken cancellationToken)
        {
            if (_visionClient == null)
            {
                throw new InvalidOperationException("PDF processing requires service account authentication. API Key mode does not support PDF.");
            }

            // For PDF, use async batch annotation
            var response = await _visionClient.DetectDocumentTextAsync(image);
            
            var text = response?.Text ?? string.Empty;
            var confidence = CalculateAverageConfidence(response);

            _logger.LogDebug(
                "[{SubmissionId}] PDF page {PageNumber} extracted {CharCount} characters",
                submissionId, pageNumber, text.Length);

            return new OcrPageDetail
            {
                PageNumber = pageNumber,
                BlobPath = blobPath,
                RawText = text,
                Confidence = confidence
            };
        }

        private static float CalculateAverageConfidence(TextAnnotation? annotation)
        {
            if (annotation?.Pages == null || !annotation.Pages.Any())
                return 0f;

            var confidences = annotation.Pages
                .SelectMany(p => p.Blocks ?? Enumerable.Empty<Block>())
                .SelectMany(b => b.Paragraphs ?? Enumerable.Empty<Paragraph>())
                .Where(p => p.Confidence > 0)
                .Select(p => p.Confidence)
                .ToList();

            return confidences.Count > 0 ? confidences.Average() : 0.8f;
        }

        private static string CombinePageTexts(List<OcrPageDetail> pages)
        {
            var sb = new StringBuilder();
            
            foreach (var page in pages.OrderBy(p => p.PageNumber))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"--- Page {page.PageNumber} ---");
                    sb.AppendLine();
                }
                sb.Append(page.RawText);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Normalize OCR text: fix common OCR errors, math symbols, fractions
        /// </summary>
        private static string NormalizeOcrText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var normalized = text;

            // Fix common OCR misreadings
            var replacements = new Dictionary<string, string>
            {
                // Math symbols
                { "×", "*" },
                { "÷", "/" },
                { "−", "-" },
                { "–", "-" },
                { "—", "-" },
                { "≠", "!=" },
                { "≤", "<=" },
                { "≥", ">=" },
                { "≈", "~=" },
                { "∞", "infinity" },
                { "π", "pi" },
                { "θ", "theta" },
                { "α", "alpha" },
                { "β", "beta" },
                { "γ", "gamma" },
                { "Δ", "delta" },
                { "Σ", "sum" },
                { "√", "sqrt" },
                
                // Common fractions
                { "½", "1/2" },
                { "⅓", "1/3" },
                { "⅔", "2/3" },
                { "¼", "1/4" },
                { "¾", "3/4" },
                { "⅕", "1/5" },
                { "⅖", "2/5" },
                { "⅗", "3/5" },
                { "⅘", "4/5" },
                
                // Common OCR errors
                { "l", "1" }, // Only in numeric context - handled separately
                { "O", "0" }, // Only in numeric context - handled separately
                { "rn", "m" },
                { "vv", "w" },
            };

            foreach (var (find, replace) in replacements)
            {
                normalized = normalized.Replace(find, replace);
            }

            // Fix common letter/number confusions in numeric context
            normalized = Regex.Replace(normalized, @"(?<=\d)[lI](?=\d)", "1");
            normalized = Regex.Replace(normalized, @"(?<=\d)[O](?=\d)", "0");

            // Normalize line endings FIRST (before whitespace normalization)
            normalized = Regex.Replace(normalized, @"\r\n|\r", "\n");

            // Normalize horizontal whitespace (spaces/tabs) but PRESERVE line breaks
            // Replace multiple spaces/tabs with single space, but keep newlines
            normalized = Regex.Replace(normalized, @"[ \t]+", " ");
            
            // Remove leading/trailing spaces from each line (but keep the lines)
            normalized = Regex.Replace(normalized, @"^[ \t]+|[ \t]+$", "", RegexOptions.Multiline);

            return normalized.Trim();
        }

        private static (string containerName, string blobName) ParseBlobPath(string blobPath)
        {
            // Handle full Azure Blob URLs like:
            // https://stsmartstudydev.blob.core.windows.net/students-answer-sheets/path/to/blob.ext
            if (blobPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                blobPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(blobPath);
                    // Path will be like /students-answer-sheets/path/to/blob.ext
                    var pathParts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
                    if (pathParts.Length == 2)
                    {
                        return (pathParts[0], pathParts[1]);
                    }
                    // Single segment - treat as blob name in default container
                    return ("students-answer-sheets", pathParts[0]);
                }
                catch
                {
                    // Fall through to relative path handling
                }
            }
            
            // Handle relative paths: "container-name/path/to/blob.ext"
            if (blobPath.StartsWith("students-answer-sheets/", StringComparison.OrdinalIgnoreCase))
            {
                return ("students-answer-sheets", blobPath.Substring("students-answer-sheets/".Length));
            }
            else if (blobPath.StartsWith("student-answers/", StringComparison.OrdinalIgnoreCase))
            {
                return ("student-answers", blobPath.Substring("student-answers/".Length));
            }
            else if (blobPath.StartsWith("written-answers/", StringComparison.OrdinalIgnoreCase))
            {
                return ("written-answers", blobPath.Substring("written-answers/".Length));
            }
            
            // Default: entire path is blob name in students-answer-sheets container
            return ("students-answer-sheets", blobPath);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    // Google Vision API Response Models (for API Key authentication via REST)
    // ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Root response from Google Vision API
    /// </summary>
    public class GoogleVisionApiResponse
    {
        public List<GoogleVisionAnnotateImageResponse>? Responses { get; set; }
    }

    public class GoogleVisionAnnotateImageResponse
    {
        public GoogleVisionFullTextAnnotation? FullTextAnnotation { get; set; }
        public GoogleVisionError? Error { get; set; }
    }

    public class GoogleVisionFullTextAnnotation
    {
        public string Text { get; set; } = string.Empty;
        public List<GoogleVisionPage>? Pages { get; set; }
    }

    public class GoogleVisionPage
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public float Confidence { get; set; }
        public List<GoogleVisionBlock>? Blocks { get; set; }
    }

    public class GoogleVisionBlock
    {
        public string BlockType { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public List<GoogleVisionParagraph>? Paragraphs { get; set; }
    }

    public class GoogleVisionParagraph
    {
        public float Confidence { get; set; }
        public List<GoogleVisionWord>? Words { get; set; }
    }

    public class GoogleVisionWord
    {
        public float Confidence { get; set; }
        public List<GoogleVisionSymbol>? Symbols { get; set; }
    }

    public class GoogleVisionSymbol
    {
        public string Text { get; set; } = string.Empty;
        public float Confidence { get; set; }
    }

    public class GoogleVisionError
    {
        public int Code { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
