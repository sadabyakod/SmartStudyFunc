using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Production OCR Service using Azure Document Intelligence
    /// Handles multi-page PDFs, images with exponential backoff retry
    /// </summary>
    public class OcrService
    {
        private readonly DocumentAnalysisClient _client;
        private readonly ILogger _logger;
        private const int MaxRetries = 3;
        private const int BaseDelayMs = 1000;

        public OcrService(ILogger logger)
        {
            _logger = logger;

            var endpoint = Environment.GetEnvironmentVariable("AZURE_FORM_RECOGNIZER_ENDPOINT");
            var apiKey = Environment.GetEnvironmentVariable("AZURE_FORM_RECOGNIZER_KEY");

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    "AZURE_FORM_RECOGNIZER_ENDPOINT and AZURE_FORM_RECOGNIZER_KEY must be set");
            }

            _client = new DocumentAnalysisClient(
                new Uri(endpoint),
                new AzureKeyCredential(apiKey)
            );
        }

        /// <summary>
        /// Extract text from image or PDF stream with cancellation support
        /// </summary>
        public async Task<string> ExtractTextFromStreamAsync(Stream documentStream, string fileName, CancellationToken ct = default)
        {
            if (documentStream == null || !documentStream.CanRead)
            {
                throw new ArgumentException("Document stream is null or cannot be read", nameof(documentStream));
            }

            _logger.LogInformation("Starting OCR extraction for: {FileName} (Size: {Size} bytes)", 
                fileName, documentStream.Length);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    // Reset stream position
                    if (documentStream.CanSeek)
                    {
                        documentStream.Position = 0;
                    }

                    // Start analysis using prebuilt-read model
                    var operation = await _client.AnalyzeDocumentAsync(
                        WaitUntil.Completed,
                        "prebuilt-read",
                        documentStream,
                        cancellationToken: ct
                    );

                    var result = operation.Value;

                    // Extract all text content from all pages
                    var extractedText = new StringBuilder();
                    int totalPages = result.Pages.Count;

                    _logger.LogInformation("OCR detected {PageCount} page(s)", totalPages);

                    foreach (var page in result.Pages)
                    {
                        _logger.LogDebug("Processing page {PageNum}/{TotalPages}", page.PageNumber, totalPages);

                        foreach (var line in page.Lines)
                        {
                            extractedText.AppendLine(line.Content);
                        }
                    }

                    var cleanedText = CleanExtractedText(extractedText.ToString());

                    _logger.LogInformation("OCR completed successfully. Extracted {CharCount} characters from {PageCount} pages", 
                        cleanedText.Length, totalPages);

                    return cleanedText;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("OCR extraction cancelled for: {FileName}", fileName);
                    throw;
                }
                catch (RequestFailedException ex) when (IsTransientError(ex) && attempt < MaxRetries)
                {
                    var delayMs = BaseDelayMs * (int)Math.Pow(2, attempt - 1);
                    _logger.LogWarning("OCR attempt {Attempt}/{MaxRetries} failed with transient error: {Error}. Retrying in {Delay}ms", 
                        attempt, MaxRetries, ex.Message, delayMs);

                    await Task.Delay(delayMs, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OCR extraction failed on attempt {Attempt}/{MaxRetries} for: {FileName}", 
                        attempt, MaxRetries, fileName);

                    if (attempt >= MaxRetries)
                    {
                        throw new InvalidOperationException(
                            $"OCR extraction failed after {MaxRetries} attempts: {ex.Message}", ex);
                    }

                    await Task.Delay(BaseDelayMs * attempt, ct);
                }
            }

            throw new InvalidOperationException($"OCR extraction failed after {MaxRetries} attempts");
        }

        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        public Task<string> ExtractTextAsync(Stream documentStream, string fileName)
        {
            return ExtractTextFromStreamAsync(documentStream, fileName, CancellationToken.None);
        }

        /// <summary>
        /// Clean and normalize extracted text
        /// </summary>
        private string CleanExtractedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var cleaned = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Skip common noise patterns
                if (IsNoisePattern(trimmed))
                {
                    continue;
                }

                cleaned.AppendLine(trimmed);
            }

            return cleaned.ToString().Trim();
        }

        /// <summary>
        /// Detect common noise patterns in OCR output
        /// </summary>
        private bool IsNoisePattern(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3)
            {
                return true;
            }

            // Skip page numbers
            if (line.All(char.IsDigit) && line.Length <= 3)
            {
                return true;
            }

            // Skip common headers/footers
            var lower = line.ToLowerInvariant();
            string[] noisePatterns = {
                "page",
                "confidential",
                "draft",
                "copyright",
                "all rights reserved"
            };

            return noisePatterns.Any(pattern => lower.Contains(pattern) && line.Length < 50);
        }

        /// <summary>
        /// Check if error is transient (retryable)
        /// </summary>
        private bool IsTransientError(RequestFailedException ex)
        {
            // Retry on throttling, timeout, and server errors
            return ex.Status == 429 || // Too many requests
                   ex.Status == 503 || // Service unavailable
                   ex.Status == 504;   // Gateway timeout
        }
    }
}
