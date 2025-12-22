using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Dual OCR Service that uses both Google Vision and Azure Document Intelligence
    /// for improved accuracy through verification and consensus.
    /// </summary>
    public interface IDualOcrService
    {
        Task<DualOcrResult> ExtractTextFromBlobsAsync(
            IEnumerable<string> blobPaths,
            Guid submissionId,
            CancellationToken cancellationToken = default);
    }

    public class DualOcrResult
    {
        public bool Success { get; set; }
        public string CombinedText { get; set; } = string.Empty;
        public string GoogleText { get; set; } = string.Empty;
        public string AzureText { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public float GoogleConfidence { get; set; }
        public float AzureConfidence { get; set; }
        public bool UsingConsensus { get; set; }
        public string PrimaryEngine { get; set; } = string.Empty;
    }

    public class DualOcrService : IDualOcrService
    {
        private readonly IGoogleVisionOcrService _googleOcr;
        private readonly OcrService _azureOcr;
        private readonly ILogger<DualOcrService> _logger;

        public DualOcrService(
            IGoogleVisionOcrService googleOcr,
            OcrService azureOcr,
            ILogger<DualOcrService> logger)
        {
            _googleOcr = googleOcr ?? throw new ArgumentNullException(nameof(googleOcr));
            _azureOcr = azureOcr ?? throw new ArgumentNullException(nameof(azureOcr));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DualOcrResult> ExtractTextFromBlobsAsync(
            IEnumerable<string> blobPaths,
            Guid submissionId,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[DUAL-OCR] Starting dual OCR extraction for SubmissionId={SubmissionId}, BlobCount={BlobCount}",
                submissionId, blobPaths.Count());

            var result = new DualOcrResult();

            try
            {
                // Run both OCR engines in parallel
                var googleTask = RunGoogleOcrAsync(blobPaths, submissionId, cancellationToken);
                var azureTask = RunAzureOcrAsync(blobPaths, submissionId, cancellationToken);

                await Task.WhenAll(googleTask, azureTask);

                var googleResult = await googleTask;
                var azureResult = await azureTask;

                result.GoogleText = googleResult.Text;
                result.AzureText = azureResult.Text;
                result.GoogleConfidence = googleResult.Confidence;
                result.AzureConfidence = azureResult.Confidence;

                // Determine which result to use based on confidence and length
                if (googleResult.Success && azureResult.Success)
                {
                    // Both succeeded - use consensus or higher confidence
                    result.Success = true;
                    result.CombinedText = CombineResults(googleResult, azureResult);
                    result.UsingConsensus = true;
                    result.PrimaryEngine = DeterminePrimaryEngine(googleResult, azureResult);

                    _logger.LogInformation(
                        "[DUAL-OCR] Both engines succeeded. GoogleLen={GoogleLen}, AzureLen={AzureLen}, GoogleConf={GoogleConf:F2}, AzureConf={AzureConf:F2}, Primary={Primary}",
                        googleResult.Text.Length, azureResult.Text.Length,
                        googleResult.Confidence, azureResult.Confidence,
                        result.PrimaryEngine);
                }
                else if (googleResult.Success)
                {
                    // Only Google succeeded
                    result.Success = true;
                    result.CombinedText = googleResult.Text;
                    result.PrimaryEngine = "Google";
                    
                    _logger.LogWarning(
                        "[DUAL-OCR] Only Google succeeded. AzureError={AzureError}",
                        azureResult.Error);
                }
                else if (azureResult.Success)
                {
                    // Only Azure succeeded
                    result.Success = true;
                    result.CombinedText = azureResult.Text;
                    result.PrimaryEngine = "Azure";
                    
                    _logger.LogWarning(
                        "[DUAL-OCR] Only Azure succeeded. GoogleError={GoogleError}",
                        googleResult.Error);
                }
                else
                {
                    // Both failed
                    result.Success = false;
                    result.ErrorMessage = $"Both OCR engines failed. Google: {googleResult.Error}, Azure: {azureResult.Error}";
                    
                    _logger.LogError(
                        "[DUAL-OCR] Both engines failed. GoogleError={GoogleError}, AzureError={AzureError}",
                        googleResult.Error, azureResult.Error);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Dual OCR error: {ex.Message}";
                
                _logger.LogError(ex,
                    "[DUAL-OCR] Unexpected error during dual OCR for SubmissionId={SubmissionId}",
                    submissionId);
            }

            return result;
        }

        private async Task<SingleOcrResult> RunGoogleOcrAsync(
            IEnumerable<string> blobPaths,
            Guid submissionId,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await _googleOcr.ExtractTextFromBlobsAsync(blobPaths, submissionId, cancellationToken);
                return new SingleOcrResult
                {
                    Success = result.Success,
                    Text = result.CombinedText,
                    Confidence = result.AverageConfidence,
                    Error = result.ErrorMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DUAL-OCR] Google OCR failed");
                return new SingleOcrResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private async Task<SingleOcrResult> RunAzureOcrAsync(
            IEnumerable<string> blobPaths,
            Guid submissionId,
            CancellationToken cancellationToken)
        {
            try
            {
                // Azure OCR needs to download blobs first
                // For now, return a placeholder - you'll need to implement blob download logic
                _logger.LogWarning("[DUAL-OCR] Azure OCR not fully implemented yet - needs blob download");
                return new SingleOcrResult
                {
                    Success = false,
                    Error = "Azure OCR requires blob download implementation"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DUAL-OCR] Azure OCR failed");
                return new SingleOcrResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private string CombineResults(SingleOcrResult google, SingleOcrResult azure)
        {
            // Strategy: Use the result with higher confidence and more content
            if (google.Confidence >= azure.Confidence && google.Text.Length >= azure.Text.Length * 0.8)
            {
                return google.Text;
            }
            else if (azure.Confidence > google.Confidence && azure.Text.Length >= google.Text.Length * 0.8)
            {
                return azure.Text;
            }
            else
            {
                // Merge both - Azure tends to be better at structured text, Google at handwriting
                var combined = new StringBuilder();
                combined.AppendLine("=== Google Vision OCR ===");
                combined.AppendLine(google.Text);
                combined.AppendLine();
                combined.AppendLine("=== Azure Document Intelligence OCR ===");
                combined.AppendLine(azure.Text);
                return combined.ToString();
            }
        }

        private string DeterminePrimaryEngine(SingleOcrResult google, SingleOcrResult azure)
        {
            if (google.Confidence > azure.Confidence + 0.1f) return "Google (higher confidence)";
            if (azure.Confidence > google.Confidence + 0.1f) return "Azure (higher confidence)";
            if (google.Text.Length > azure.Text.Length * 1.2) return "Google (more content)";
            if (azure.Text.Length > google.Text.Length * 1.2) return "Azure (more content)";
            return "Consensus (both similar)";
        }

        private class SingleOcrResult
        {
            public bool Success { get; set; }
            public string Text { get; set; } = string.Empty;
            public float Confidence { get; set; }
            public string? Error { get; set; }
        }
    }
}
