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
                            new ChatRequestSystemMessage(@"You are a STRICT school exam evaluator following Indian Board Exam standards.

YOUR ROLE:
- Evaluate each step of student's answer against the model (rubric) step
- Assign marks for each step
- Calculate the total score
- Follow CBSE/State Board correction standards strictly

STRICT RULES:
- Each step carries EXACTLY 1 mark (or as defined)
- Be STRICT and DETERMINISTIC
- Do NOT infer missing steps
- Do NOT be generous
- Judge ONLY what is written by the student
- Same input MUST always produce the same output
- Copied formula without working → 0 marks for that step
- Incomplete step → 0 marks (no partial within a step)
- Wrong method even with right answer → 0 marks for method step

MARKING STANDARDS:
Step Type        | Full Marks (1) | Zero Marks (0)
----------------|----------------|----------------
Formula/Method  | Correct formula written | Missing/wrong formula
Substitution    | Correct values substituted | Wrong/no substitution
Calculation     | Correct arithmetic | Wrong calculation
Final Answer    | Correct with units | Wrong answer/no units

Return STRICT JSON ONLY. No markdown. No explanations outside JSON."),
                            new ChatRequestUserMessage(prompt)
                        },
                        Temperature = 0.0f,  // Zero temperature for deterministic evaluation
                        MaxTokens = 1200,
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
                        UsedFallback = false,
                        IsComplete = parsed.IsComplete,
                        CompletionStatus = parsed.CompletionStatus ?? (parsed.IsComplete ? "Complete" : "Incomplete")
                    };

                    // Parse step-wise breakdown
                    if (parsed.StepWiseBreakdown != null && parsed.StepWiseBreakdown.Any())
                    {
                        result.StepWiseBreakdown = parsed.StepWiseBreakdown.Select(s => new StepWiseMarks
                        {
                            StepNumber = s.StepNumber,
                            StepDescription = s.StepDescription,
                            MaxMarks = s.MaxMarks,
                            MarksAwarded = s.MarksAwarded,
                            Status = s.Status,
                            Feedback = s.Feedback
                        }).ToList();
                    }

                    // Add keyword analysis
                    var keywordAnalysis = AnalyzeKeywords(studentText, keywords);
                    result.KeywordsMatched = keywordAnalysis.Matched;
                    result.MissingKeywords = keywordAnalysis.Missing;

                    _logger.LogInformation("AI scoring complete. Score: {Score}/{MaxMarks}, Complete: {IsComplete}, Steps: {StepCount}", 
                        result.Score, maxMarks, result.IsComplete, result.StepWiseBreakdown.Count);

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

            // Determine completion status
            bool isComplete = keywordScore >= 0.8 && lengthFactor >= 0.7;
            string completionStatus = keywordScore >= 0.8 ? "Complete" : (keywordScore >= 0.4 ? "Partial" : "Incomplete");

            // Generate step-wise breakdown based on keywords
            var stepWiseBreakdown = new List<StepWiseMarks>();
            double marksPerKeyword = keywords.Count > 0 ? (double)maxMarks / keywords.Count : maxMarks;
            int stepNum = 1;
            
            foreach (var keyword in keywords)
            {
                bool matched = keywordAnalysis.Matched.Contains(keyword);
                stepWiseBreakdown.Add(new StepWiseMarks
                {
                    StepNumber = stepNum++,
                    StepDescription = $"Concept: {keyword}",
                    MaxMarks = Math.Round(marksPerKeyword, 1),
                    MarksAwarded = matched ? Math.Round(marksPerKeyword, 1) : 0,
                    Status = matched ? "Complete" : "Missing",
                    Feedback = matched ? $"'{keyword}' concept covered" : $"'{keyword}' concept not found in answer"
                });
            }

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
                UsedFallback = true,
                IsComplete = isComplete,
                CompletionStatus = completionStatus,
                StepWiseBreakdown = stepWiseBreakdown
            };
        }

        /// <summary>
        /// Build STRICT Indian Board Exam evaluation prompt
        /// Each step = 1 mark, deterministic scoring
        /// </summary>
        private string BuildKarnatakaPucPrompt(
            string studentAnswer,
            string idealAnswer,
            int maxMarks,
            List<string> keywords)
        {
            var keywordsList = keywords.Any() ? string.Join(", ", keywords) : "Not specified";

            return $@"You are a STRICT school exam evaluator. Evaluate this answer following Indian Board Exam standards.

**MODEL ANSWER (RUBRIC):**
{idealAnswer}

**STUDENT'S ANSWER:**
{studentAnswer}

**MAXIMUM MARKS:** {maxMarks}
**KEY CONCEPTS:** {keywordsList}

**STRICT EVALUATION RULES:**
1. Each step carries EXACTLY 1 mark (total steps = {maxMarks})
2. Be STRICT - do NOT be generous
3. Do NOT infer missing steps
4. Judge ONLY what is written
5. Same input MUST always produce same output

**STEP-WISE MARKING:**
- Step written correctly and completely → 1 mark
- Step missing, wrong, or incomplete → 0 marks
- NO partial marks within a step

**MARKING CRITERIA:**
| Step Type | 1 Mark (Full) | 0 Marks (Zero) |
|-----------|---------------|----------------|
| Formula/Method | Correct formula written | Missing/wrong |
| Substitution | Correct values | Wrong values |
| Calculation | Correct arithmetic | Wrong calculation |
| Final Answer | Correct with units | Wrong/no units |

**REQUIRED JSON RESPONSE (STRICT FORMAT):**
{{
  ""score"": <integer 0 to {maxMarks}>,
  ""isComplete"": <true/false>,
  ""completionStatus"": ""Complete"" | ""Partial"" | ""Incomplete"",
  ""stepWiseBreakdown"": [
    {{
      ""stepNumber"": 1,
      ""stepDescription"": ""<step description>"",
      ""maxMarks"": 1,
      ""marksAwarded"": <0 or 1>,
      ""status"": ""Correct"" | ""Incorrect"" | ""Missing"",
      ""feedback"": ""<reason for marks>""
    }}
  ],
  ""feedback"": ""<overall summary>"",
  ""missingPoints"": [""<missing items>""],
  ""strengths"": [""<correct items>""],
  ""improvement"": ""<suggestion>""
}}

Return STRICT JSON ONLY. No markdown.";
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

            [JsonProperty("isComplete")]
            public bool IsComplete { get; set; } = true;

            [JsonProperty("completionStatus")]
            public string? CompletionStatus { get; set; }

            [JsonProperty("completionPercentage")]
            public double CompletionPercentage { get; set; } = 100;

            [JsonProperty("stepWiseBreakdown")]
            public List<StepWiseMarksDto>? StepWiseBreakdown { get; set; }
        }

        private class StepWiseMarksDto
        {
            [JsonProperty("stepNumber")]
            public int StepNumber { get; set; }

            [JsonProperty("stepDescription")]
            public string StepDescription { get; set; } = string.Empty;

            [JsonProperty("maxMarks")]
            public double MaxMarks { get; set; }

            [JsonProperty("marksAwarded")]
            public double MarksAwarded { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; } = string.Empty;

            [JsonProperty("feedback")]
            public string Feedback { get; set; } = string.Empty;
        }

        private class KeywordAnalysis
        {
            public List<string> Matched { get; set; } = new List<string>();
            public List<string> Missing { get; set; } = new List<string>();
        }
    }
}
