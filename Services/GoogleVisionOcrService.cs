using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Logging;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Service for performing OCR using Google Cloud Vision API
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
        private readonly ImageAnnotatorClient _visionClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<GoogleVisionOcrService> _logger;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(60);

        public GoogleVisionOcrService(
            ImageAnnotatorClient visionClient,
            BlobServiceClient blobServiceClient,
            ILogger<GoogleVisionOcrService> logger)
        {
            _visionClient = visionClient;
            _blobServiceClient = blobServiceClient;
            _logger = logger;
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
            var image = Image.FromBytes(imageBytes);

            // Check if PDF (multi-page support)
            if (blobPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return await ProcessPdfAsync(image, blobPath, pageNumber, submissionId, cancellationToken);
            }

            // Process single image
            var response = await _visionClient.DetectDocumentTextAsync(image);
            
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

            // Normalize whitespace
            normalized = Regex.Replace(normalized, @"\s+", " ");
            normalized = Regex.Replace(normalized, @"^\s+|\s+$", "", RegexOptions.Multiline);

            // Normalize line endings
            normalized = Regex.Replace(normalized, @"\r\n|\r", "\n");

            return normalized.Trim();
        }

        private static (string containerName, string blobName) ParseBlobPath(string blobPath)
        {
            // Expected format: "container/path/to/blob.ext" or just path if container is known
            var parts = blobPath.Split('/', 2);
            
            if (parts.Length == 2)
            {
                return (parts[0], parts[1]);
            }
            
            // Default container
            return ("written-answers", blobPath);
        }
    }
}
