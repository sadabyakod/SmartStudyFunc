using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// GPT-4 Vision OCR Service for extracting handwritten text from exam answer sheets.
    /// Uses Azure OpenAI GPT-4 Vision to extract answers with question number identification.
    /// </summary>
    public interface IGpt4VisionOcrService
    {
        /// <summary>
        /// Extract handwritten answers from images using GPT-4 Vision.
        /// Returns structured data with question numbers and extracted text.
        /// </summary>
        Task<VisionOcrResult> ExtractAnswersFromImagesAsync(
            List<string> blobPaths, 
            Guid submissionId,
            CancellationToken cancellationToken = default);
    }

    public class Gpt4VisionOcrService : IGpt4VisionOcrService
    {
        private readonly HttpClient _httpClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _deploymentName;
        private readonly ILogger<Gpt4VisionOcrService> _logger;

        private const string OCR_SYSTEM_PROMPT = @"You are an expert OCR system specialized in extracting handwritten answers from exam answer sheets.

Your task is to:
1. Extract ALL handwritten text from the image
2. Identify question numbers (Q1, Q2, 1., 2., etc.)
3. Match each answer to its corresponding question number
4. Preserve the exact text as written by the student

Return your response as valid JSON in this exact format:
{
  ""answers"": [
    {
      ""questionNumber"": ""1"",
      ""answerText"": ""The extracted answer text for question 1...""
    },
    {
      ""questionNumber"": ""2"",
      ""answerText"": ""The extracted answer text for question 2...""
    }
  ],
  ""fullText"": ""Complete extracted text from the entire page..."",
  ""confidence"": 0.95,
  ""notes"": ""Any observations about legibility or issues""
}

Important:
- Extract text EXACTLY as written (preserve spelling/grammar)
- If you cannot identify question numbers, use 'unknown_1', 'unknown_2', etc.
- Include ALL visible text in the fullText field
- Set confidence between 0.0 and 1.0 based on legibility";

        public Gpt4VisionOcrService(
            BlobServiceClient blobServiceClient,
            IConfiguration configuration,
            ILogger<Gpt4VisionOcrService> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;

            _endpoint = configuration["AzureOpenAI:Endpoint"] 
                ?? throw new ArgumentNullException("AzureOpenAI:Endpoint not configured");
            _apiKey = configuration["AzureOpenAI:ApiKey"]
                ?? throw new ArgumentNullException("AzureOpenAI:ApiKey not configured");
            _deploymentName = configuration["AzureOpenAI:VisionDeployment"] 
                ?? configuration["AzureOpenAI:ChatDeployment"]
                ?? "gpt-4o";

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5) // Vision processing can be slow
            };
        }

        public async Task<VisionOcrResult> ExtractAnswersFromImagesAsync(
            List<string> blobPaths,
            Guid submissionId,
            CancellationToken cancellationToken = default)
        {
            var result = new VisionOcrResult
            {
                SubmissionId = submissionId,
                ProcessedAt = DateTime.UtcNow,
                ExtractedAnswers = new List<ExtractedAnswer>(),
                PageResults = new List<PageOcrResult>()
            };

            try
            {
                _logger.LogInformation(
                    "[GPT4_VISION_OCR] Starting extraction for SubmissionId={SubmissionId}, Images={ImageCount}",
                    submissionId, blobPaths.Count);

                var fullTextBuilder = new System.Text.StringBuilder();

                foreach (var blobPath in blobPaths)
                {
                    try
                    {
                        var pageResult = await ProcessSingleImageAsync(blobPath, cancellationToken);
                        result.PageResults.Add(pageResult);

                        if (pageResult.Success)
                        {
                            result.ExtractedAnswers.AddRange(pageResult.Answers);
                            fullTextBuilder.AppendLine(pageResult.FullText);
                            fullTextBuilder.AppendLine("---PAGE BREAK---");
                        }
                        else
                        {
                            _logger.LogWarning(
                                "[GPT4_VISION_OCR] Failed to process image: {BlobPath}, Error: {Error}",
                                blobPath, pageResult.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, 
                            "[GPT4_VISION_OCR] Error processing image: {BlobPath}", blobPath);
                        
                        result.PageResults.Add(new PageOcrResult
                        {
                            BlobPath = blobPath,
                            Success = false,
                            ErrorMessage = ex.Message
                        });
                    }
                }

                result.FullExtractedText = fullTextBuilder.ToString();
                result.Success = result.PageResults.Exists(p => p.Success);
                result.TotalAnswersExtracted = result.ExtractedAnswers.Count;

                _logger.LogInformation(
                    "[GPT4_VISION_OCR] Completed for SubmissionId={SubmissionId}, Answers={AnswerCount}, Success={Success}",
                    submissionId, result.TotalAnswersExtracted, result.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "[GPT4_VISION_OCR] Critical error for SubmissionId={SubmissionId}", submissionId);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private async Task<PageOcrResult> ProcessSingleImageAsync(
            string blobPath,
            CancellationToken cancellationToken)
        {
            var pageResult = new PageOcrResult
            {
                BlobPath = blobPath,
                Answers = new List<ExtractedAnswer>()
            };

            try
            {
                // Download image from blob storage
                var imageBytes = await DownloadBlobAsync(blobPath, cancellationToken);
                var base64Image = Convert.ToBase64String(imageBytes);
                var mimeType = GetMimeType(blobPath);

                _logger.LogInformation(
                    "[GPT4_VISION_OCR] Processing image: {BlobPath}, Size: {Size}KB",
                    blobPath, imageBytes.Length / 1024);

                // Call GPT-4 Vision API
                var requestBody = new
                {
                    messages = new object[]
                    {
                        new
                        {
                            role = "system",
                            content = OCR_SYSTEM_PROMPT
                        },
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new
                                {
                                    type = "text",
                                    text = "Extract all handwritten answers from this exam answer sheet. Identify question numbers and extract the complete text for each answer."
                                },
                                new
                                {
                                    type = "image_url",
                                    image_url = new
                                    {
                                        url = $"data:{mimeType};base64,{base64Image}",
                                        detail = "high"
                                    }
                                }
                            }
                        }
                    },
                    max_tokens = 4000,
                    temperature = 0.1
                };

                var apiUrl = $"{_endpoint.TrimEnd('/')}/openai/deployments/{_deploymentName}/chat/completions?api-version=2024-02-15-preview";

                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Headers.Add("api-key", _apiKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    System.Text.Encoding.UTF8,
                    "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "[GPT4_VISION_OCR] API Error: {StatusCode}, Response: {Response}",
                        response.StatusCode, responseContent.Substring(0, Math.Min(500, responseContent.Length)));
                    
                    pageResult.Success = false;
                    pageResult.ErrorMessage = $"API Error: {response.StatusCode}";
                    return pageResult;
                }

                // Parse response
                var apiResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent);
                var assistantContent = apiResponse?.Choices?[0]?.Message?.Content ?? "";

                // Extract JSON from response (handle markdown code blocks)
                var jsonContent = ExtractJsonFromResponse(assistantContent);
                var ocrOutput = JsonSerializer.Deserialize<VisionOcrOutput>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (ocrOutput?.Answers != null)
                {
                    foreach (var answer in ocrOutput.Answers)
                    {
                        pageResult.Answers.Add(new ExtractedAnswer
                        {
                            QuestionNumber = answer.QuestionNumber,
                            AnswerText = answer.AnswerText,
                            SourcePage = blobPath
                        });
                    }
                }

                pageResult.FullText = ocrOutput?.FullText ?? assistantContent;
                pageResult.Confidence = ocrOutput?.Confidence ?? 0.8;
                pageResult.Notes = ocrOutput?.Notes;
                pageResult.Success = true;

                _logger.LogInformation(
                    "[GPT4_VISION_OCR] Extracted {AnswerCount} answers from {BlobPath}",
                    pageResult.Answers.Count, blobPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GPT4_VISION_OCR] Error processing: {BlobPath}", blobPath);
                pageResult.Success = false;
                pageResult.ErrorMessage = ex.Message;
            }

            return pageResult;
        }

        private async Task<byte[]> DownloadBlobAsync(string blobPath, CancellationToken cancellationToken)
        {
            // Parse blob path: container/path/to/file.jpg
            var parts = blobPath.Split('/', 2);
            var containerName = parts.Length > 1 ? parts[0] : "student-answers";
            var blobName = parts.Length > 1 ? parts[1] : blobPath;

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            using var memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }

        private static string GetMimeType(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "image/jpeg"
            };
        }

        private static string ExtractJsonFromResponse(string content)
        {
            // Remove markdown code blocks if present
            content = content.Trim();
            
            if (content.StartsWith("```json"))
                content = content.Substring(7);
            else if (content.StartsWith("```"))
                content = content.Substring(3);
            
            if (content.EndsWith("```"))
                content = content.Substring(0, content.Length - 3);
            
            return content.Trim();
        }
    }

    #region Models

    public class VisionOcrResult
    {
        public Guid SubmissionId { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ProcessedAt { get; set; }
        public List<ExtractedAnswer> ExtractedAnswers { get; set; } = new();
        public List<PageOcrResult> PageResults { get; set; } = new();
        public string FullExtractedText { get; set; } = "";
        public int TotalAnswersExtracted { get; set; }
    }

    public class PageOcrResult
    {
        public string BlobPath { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string FullText { get; set; } = "";
        public double Confidence { get; set; }
        public string? Notes { get; set; }
        public List<ExtractedAnswer> Answers { get; set; } = new();
    }

    public class ExtractedAnswer
    {
        public string QuestionNumber { get; set; } = "";
        public string AnswerText { get; set; } = "";
        public string SourcePage { get; set; } = "";
    }

    public class VisionOcrOutput
    {
        [JsonPropertyName("answers")]
        public List<VisionAnswer>? Answers { get; set; }
        
        [JsonPropertyName("fullText")]
        public string? FullText { get; set; }
        
        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
        
        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }

    public class VisionAnswer
    {
        [JsonPropertyName("questionNumber")]
        public string QuestionNumber { get; set; } = "";
        
        [JsonPropertyName("answerText")]
        public string AnswerText { get; set; } = "";
    }

    public class OpenAIResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenAIChoice>? Choices { get; set; }
    }

    public class OpenAIChoice
    {
        [JsonPropertyName("message")]
        public OpenAIMessage? Message { get; set; }
    }

    public class OpenAIMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    #endregion
}
