using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SmartStudyFunc.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Production AI Scoring Service for Karnataka PUC Mathematics evaluation
    /// Implements exponential backoff and fallback scoring
    /// </summary>
    public class AiScoringService
    {
        private readonly OpenAIClient _openAiClient;
        private readonly string _deploymentName;
        private readonly ILogger _logger;
        private const int MaxRetries = 3;
        private const int BaseDelayMs = 2000;

        public AiScoringService(ILogger logger)
        {
            _logger = logger;

            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            _deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    "AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_KEY must be set");
            }

            _openAiClient = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        }

        /// <summary>
        /// Score student answer with AI evaluation and fallback
        /// </summary>
        public async Task<ScoringResult> ScoreAsync(
            string studentText,
            string idealAnswer,
            int maxMarks,
            IEnumerable<string> keywords,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(studentText))
            {
                throw new ArgumentException("Student answer text cannot be empty", nameof(studentText));
            }

            _logger.LogInformation("Starting AI scoring evaluation for answer (Length: {Length})", studentText.Length);

            var keywordList = keywords?.ToList() ?? new List<string>();

            try
            {
                // Try AI evaluation with retry logic
                return await ScoreWithAiAsync(studentText, idealAnswer, maxMarks, keywordList, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI scoring failed, falling back to keyword-based scoring");

                // Fallback to keyword-based scoring
                return ScoreWithKeywordFallback(studentText, idealAnswer, maxMarks, keywordList);
            }
        }

        /// <summary>
        /// AI-based scoring with Karnataka PUC Mathematics evaluation criteria
        /// </summary>
        private async Task<ScoringResult> ScoreWithAiAsync(
            string studentText,
            string idealAnswer,
            int maxMarks,
            List<string> keywords,
            CancellationToken ct)
        {
            var prompt = BuildKarnatakaPucPrompt(studentText, idealAnswer, maxMarks, keywords);

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var chatCompletionsOptions = new ChatCompletionsOptions
                    {
                        DeploymentName = _deploymentName,
                        Messages =
                        {
                            new ChatRequestSystemMessage(@"You are a strict Karnataka PUC Mathematics examiner. 
You evaluate answers based on: 
1) Mathematical correctness and accuracy
2) Step-by-step methodology
3) Use of correct formulas and theorems
4) Clear presentation and notation
5) Coverage of key concepts

Award marks strictly. Partial marks only for partial understanding.
Respond ONLY with valid JSON, no markdown, no explanations."),
                            new ChatRequestUserMessage(prompt)
                        },
                        Temperature = 0.2f,  // Low temperature for consistent evaluation
                        MaxTokens = 1500,
                        ResponseFormat = ChatCompletionsResponseFormat.JsonObject
                    };

                    _logger.LogInformation("Calling OpenAI API (Attempt {Attempt}/{MaxRetries})", attempt, MaxRetries);

                    var response = await _openAiClient.GetChatCompletionsAsync(chatCompletionsOptions, ct);
                    var aiResult = response.Value.Choices[0].Message.Content;

                    _logger.LogDebug("AI Response received: {Response}", aiResult);

                    // Parse AI JSON response
                    var parsed = JsonConvert.DeserializeObject<AiScoringResponse>(aiResult);

                    if (parsed == null)
                    {
                        throw new InvalidOperationException("Failed to parse AI response");
                    }

                    // Build result
                    var result = new ScoringResult
                    {
                        Score = Math.Max(0, Math.Min(parsed.Score, maxMarks)),
                        MaxMarks = maxMarks,
                        Feedback = parsed.Feedback ?? "No feedback provided",
                        MissingPoints = parsed.MissingPoints ?? new List<string>(),
                        Strengths = parsed.Strengths ?? new List<string>(),
                        ImprovementSuggestion = parsed.Improvement ?? "Keep practicing",
                        UsedFallback = false
                    };

                    // Add keyword analysis
                    var keywordAnalysis = AnalyzeKeywords(studentText, keywords);
                    result.KeywordsMatched = keywordAnalysis.Matched;
                    result.MissingKeywords = keywordAnalysis.Missing;

                    _logger.LogInformation("AI scoring complete. Score: {Score}/{MaxMarks}", result.Score, maxMarks);

                    return result;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("AI scoring cancelled");
                    throw;
                }
                catch (RequestFailedException ex) when (IsTransientError(ex) && attempt < MaxRetries)
                {
                    var delayMs = BaseDelayMs * (int)Math.Pow(2, attempt - 1);
                    _logger.LogWarning("OpenAI attempt {Attempt}/{MaxRetries} failed: {Error}. Retrying in {Delay}ms",
                        attempt, MaxRetries, ex.Message, delayMs);

                    await Task.Delay(delayMs, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI scoring attempt {Attempt}/{MaxRetries} failed", attempt, MaxRetries);

                    if (attempt >= MaxRetries)
                    {
                        throw;
                    }

                    await Task.Delay(BaseDelayMs * attempt, ct);
                }
            }

            throw new InvalidOperationException($"AI scoring failed after {MaxRetries} attempts");
        }

        /// <summary>
        /// Fallback keyword-based scoring when AI is unavailable
        /// </summary>
        private ScoringResult ScoreWithKeywordFallback(
            string studentText,
            string idealAnswer,
            int maxMarks,
            List<string> keywords)
        {
            _logger.LogInformation("Using fallback keyword-based scoring");

            var keywordAnalysis = AnalyzeKeywords(studentText, keywords);

            // Calculate score based on keyword coverage
            double keywordScore = keywords.Count > 0
                ? (double)keywordAnalysis.Matched.Count / keywords.Count
                : 0.5;

            // Simple length-based adjustment (longer answers get slight bonus)
            double lengthFactor = Math.Min(1.0, studentText.Length / Math.Max(1.0, idealAnswer.Length));

            // Combine factors
            double finalScore = Math.Round(maxMarks * keywordScore * (0.7 + 0.3 * lengthFactor), 1);

            return new ScoringResult
            {
                Score = Math.Max(0, Math.Min(finalScore, maxMarks)),
                MaxMarks = maxMarks,
                Feedback = $"Automated scoring: {keywordAnalysis.Matched.Count}/{keywords.Count} key concepts identified.",
                MissingPoints = keywordAnalysis.Missing,
                Strengths = keywordAnalysis.Matched.Any()
                    ? new List<string> { $"Covered {keywordAnalysis.Matched.Count} key concepts" }
                    : new List<string>(),
                ImprovementSuggestion = keywordAnalysis.Missing.Any()
                    ? $"Include these concepts: {string.Join(", ", keywordAnalysis.Missing.Take(3))}"
                    : "Good coverage of key concepts",
                KeywordsMatched = keywordAnalysis.Matched,
                MissingKeywords = keywordAnalysis.Missing,
                UsedFallback = true
            };
        }

        /// <summary>
        /// Build Karnataka PUC Mathematics-specific evaluation prompt
        /// </summary>
        private string BuildKarnatakaPucPrompt(
            string studentAnswer,
            string idealAnswer,
            int maxMarks,
            List<string> keywords)
        {
            var keywordsList = keywords.Any() ? string.Join(", ", keywords) : "Not specified";

            return $@"Evaluate this Karnataka PUC Mathematics answer strictly.

**Ideal Answer:**
{idealAnswer}

**Student's Answer:**
{studentAnswer}

**Maximum Marks:** {maxMarks}
**Key Concepts:** {keywordsList}

**Evaluation Criteria:**
1. Mathematical correctness (40%)
2. Step-by-step working (30%)
3. Use of correct formulas/theorems (20%)
4. Presentation and notation (10%)

**Instructions:**
- Award marks strictly based on Karnataka PUC standards
- Partial marks only for partial correct working
- Deduct marks for incorrect steps or missing work
- Check for all key concepts

**Required JSON Response Format:**
{{
  ""score"": <number 0 to {maxMarks}>,
  ""feedback"": ""<2-3 sentences explaining the score>"",
  ""missingPoints"": [""<concept 1>"", ""<concept 2>""],
  ""strengths"": [""<strength 1>"", ""<strength 2>""],
  ""improvement"": ""<specific suggestion>""
}}

Respond ONLY with valid JSON.";
        }

        /// <summary>
        /// Analyze keyword coverage in student answer
        /// </summary>
        private KeywordAnalysis AnalyzeKeywords(string studentAnswer, List<string> keywords)
        {
            if (keywords == null || !keywords.Any())
            {
                return new KeywordAnalysis
                {
                    Matched = new List<string>(),
                    Missing = new List<string>()
                };
            }

            var answerLower = studentAnswer.ToLowerInvariant();
            var matched = new List<string>();
            var missing = new List<string>();

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;

                if (answerLower.Contains(keyword.ToLowerInvariant()))
                {
                    matched.Add(keyword);
                }
                else
                {
                    missing.Add(keyword);
                }
            }

            return new KeywordAnalysis
            {
                Matched = matched,
                Missing = missing
            };
        }

        private bool IsTransientError(RequestFailedException ex)
        {
            return ex.Status == 429 || ex.Status == 503 || ex.Status == 504;
        }

        // Internal DTO classes
        private class AiScoringResponse
        {
            [JsonProperty("score")]
            public double Score { get; set; }

            [JsonProperty("feedback")]
            public string? Feedback { get; set; }

            [JsonProperty("missingPoints")]
            public List<string>? MissingPoints { get; set; }

            [JsonProperty("strengths")]
            public List<string>? Strengths { get; set; }

            [JsonProperty("improvement")]
            public string? Improvement { get; set; }
        }

        private class KeywordAnalysis
        {
            public List<string> Matched { get; set; } = new List<string>();
            public List<string> Missing { get; set; } = new List<string>();
        }
    }
}
