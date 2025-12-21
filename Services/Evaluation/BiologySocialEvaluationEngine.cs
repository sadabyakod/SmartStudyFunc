using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// Biology and Social Science evaluation engine
    /// CRITICAL: Uses ONLY syllabus content from Azure Blob
    /// Blocks outside-syllabus knowledge strictly
    /// </summary>
    public class BiologySocialEvaluationEngine : IEvaluationEngine
    {
        private readonly ILogger<BiologySocialEvaluationEngine> _logger;
        private readonly OpenAiService _openAiService;
        private readonly EmbeddingService _embeddingService;
        private readonly BlobServiceClient _blobServiceClient;

        public string EngineName => "Biology/Social Science Syllabus-Based Engine";

        public BiologySocialEvaluationEngine(
            ILogger<BiologySocialEvaluationEngine> logger,
            OpenAiService openAiService,
            EmbeddingService embeddingService,
            BlobServiceClient blobServiceClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
        }

        public bool CanHandle(SubjectCategory subject, QuestionType questionType)
        {
            return (subject == SubjectCategory.Biology || subject == SubjectCategory.SocialScience) &&
                   (questionType == QuestionType.Definition ||
                    questionType == QuestionType.ShortAnswer ||
                    questionType == QuestionType.LongAnswer ||
                    questionType == QuestionType.Essay);
        }

        public async Task<EvaluationEngineResult> EvaluateAsync(
            EvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "{Subject} engine evaluating {QuestionType} question: {QuestionId}",
                context.Subject, context.Type, context.QuestionId);

            var result = new EvaluationEngineResult
            {
                MaxMarks = context.MaxMarks,
                ProcessedBy = EngineName,
                AuditTrail = new Dictionary<string, object>
                {
                    ["Subject"] = context.Subject.ToString(),
                    ["SyllabusRestricted"] = true,
                    ["EvaluationTimestamp"] = DateTime.UtcNow
                }
            };

            try
            {
                // Step 1: Load syllabus content from Azure Blob (MANDATORY)
                var syllabusContent = await LoadSyllabusContentAsync(
                    context.SyllabusReference,
                    context.Subject,
                    context.ClassLevel,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(syllabusContent))
                {
                    _logger.LogWarning("No syllabus content found for {Subject} Class {Class}",
                        context.Subject, context.ClassLevel);
                    result.NeedsReview = true;
                    result.ConfidenceScore = 0.3;
                    result.EvaluationReason = "Syllabus content not available - cannot evaluate";
                    return result;
                }

                result.AuditTrail["SyllabusLength"] = syllabusContent.Length;
                result.AuditTrail["SyllabusSource"] = context.SyllabusReference;

                // Step 2: Extract expected key points from model answer and syllabus
                var expectedPoints = await ExtractKeyPointsAsync(
                    context.ModelAnswer,
                    syllabusContent,
                    cancellationToken);

                result.AuditTrail["ExpectedPoints"] = expectedPoints;

                // Step 3: Check student answer against syllabus-only content
                var (coverageScore, matchedPoints, missedPoints) = await CalculateKnowledgeCoverageAsync(
                    context.StudentAnswer,
                    expectedPoints,
                    syllabusContent,
                    cancellationToken);

                result.MatchedKeywords = matchedPoints;
                result.MissingKeywords = missedPoints;

                // Step 4: Verify no outside-syllabus content is present
                var outsideSyllabusCheck = await DetectOutsideSyllabusContentAsync(
                    context.StudentAnswer,
                    syllabusContent,
                    cancellationToken);

                if (outsideSyllabusCheck.HasOutsideContent)
                {
                    _logger.LogWarning("Outside-syllabus content detected: {Content}",
                        string.Join(", ", outsideSyllabusCheck.OutsideElements));
                    result.AuditTrail["OutsideSyllabusWarning"] = outsideSyllabusCheck.OutsideElements;
                    // Do NOT deduct marks, but flag for teacher review
                    result.NeedsReview = true;
                }

                // Step 5: Calculate marks based on syllabus-validated coverage
                result.MarksAwarded = context.MaxMarks * coverageScore;
                result.ConfidenceScore = outsideSyllabusCheck.HasOutsideContent ? 0.7 : 0.85;
                result.EvaluationReason = $"Syllabus-based coverage: {matchedPoints.Count}/{expectedPoints.Count} key points ({coverageScore:P0})";

                // Step 6: Build feedback
                if (coverageScore >= 0.8)
                {
                    result.Strengths.Add("Good coverage of syllabus-defined concepts");
                }
                else if (coverageScore >= 0.5)
                {
                    result.Strengths.Add("Partial understanding of syllabus concepts");
                    result.Improvements.Add($"Cover these syllabus points: {string.Join(", ", missedPoints.Take(3))}");
                }
                else
                {
                    result.Improvements.Add("Review the syllabus content carefully");
                    result.NeedsReview = true;
                }

                // Step 7: Generate contextual feedback (OpenAI - restricted to syllabus)
                result.StudentFeedback = await GenerateSyllabusRestrictedFeedbackAsync(
                    context,
                    result,
                    syllabusContent,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Biology/Social evaluation failed for {QuestionId}", context.QuestionId);
                result.NeedsReview = true;
                result.ConfidenceScore = 0;
                result.EvaluationReason = $"Evaluation error: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Loads syllabus content from Azure Blob Storage
        /// This is the SINGLE SOURCE OF TRUTH
        /// </summary>
        private async Task<string> LoadSyllabusContentAsync(
            string syllabusReference,
            SubjectCategory subject,
            int classLevel,
            CancellationToken cancellationToken)
        {
            try
            {
                string blobPath;

                // If explicit reference provided, use it
                if (!string.IsNullOrWhiteSpace(syllabusReference))
                {
                    blobPath = syllabusReference;
                }
                else
                {
                    // Construct default path: syllabus/class-{level}/{subject}.txt
                    blobPath = $"syllabus/class-{classLevel}/{subject.ToString().ToLowerInvariant()}.txt";
                }

                _logger.LogInformation("Loading syllabus from blob: {BlobPath}", blobPath);

                var containerClient = _blobServiceClient.GetBlobContainerClient("syllabus");
                var blobClient = containerClient.GetBlobClient(blobPath);

                if (!await blobClient.ExistsAsync(cancellationToken))
                {
                    _logger.LogWarning("Syllabus blob not found: {BlobPath}", blobPath);
                    return string.Empty;
                }

                var response = await blobClient.DownloadContentAsync(cancellationToken);
                var content = response.Value.Content.ToString();

                _logger.LogInformation("Loaded {Length} chars from syllabus blob", content.Length);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load syllabus content");
                return string.Empty;
            }
        }

        /// <summary>
        /// Extracts key points from model answer using syllabus context
        /// </summary>
        private async Task<List<string>> ExtractKeyPointsAsync(
            string modelAnswer,
            string syllabusContent,
            CancellationToken cancellationToken)
        {
            try
            {
                var prompt = $@"Extract the key factual points from this model answer that are relevant to the syllabus.

Syllabus Content:
{syllabusContent.Substring(0, Math.Min(1000, syllabusContent.Length))}

Model Answer:
{modelAnswer}

Return ONLY a JSON array of key points (max 10):
[""point 1"", ""point 2"", ...]";

                var response = await _openAiService.GetChatCompletionAsync(prompt);
                if (string.IsNullOrWhiteSpace(response))
                    return modelAnswer.Split('.', '\n')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 10)
                        .Take(10)
                        .ToList();

                var points = JsonConvert.DeserializeObject<List<string>>(response) ?? new();
                return points.Take(10).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract key points, using sentence split");
                return modelAnswer.Split('.', '\n')
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 10)
                    .Take(10)
                    .ToList();
            }
        }

        /// <summary>
        /// Calculates how many expected points the student covered
        /// Uses semantic similarity + keyword matching
        /// </summary>
        private async Task<(double Score, List<string> Matched, List<string> Missed)> CalculateKnowledgeCoverageAsync(
            string studentAnswer,
            List<string> expectedPoints,
            string syllabusContent,
            CancellationToken cancellationToken)
        {
            var matched = new List<string>();
            var missed = new List<string>();

            var studentLower = studentAnswer.ToLowerInvariant();

            foreach (var point in expectedPoints)
            {
                // Simple keyword presence check
                var pointWords = point.ToLowerInvariant()
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3)
                    .ToList();

                var wordMatchCount = pointWords.Count(w => studentLower.Contains(w));
                var matchRatio = pointWords.Count > 0 ? (double)wordMatchCount / pointWords.Count : 0;

                if (matchRatio >= 0.5)
                {
                    matched.Add(point);
                }
                else
                {
                    missed.Add(point);
                }
            }

            var score = expectedPoints.Count > 0
                ? (double)matched.Count / expectedPoints.Count
                : 0.5;

            return (score, matched, missed);
        }

        /// <summary>
        /// Detects if student included information not present in syllabus
        /// CRITICAL: Prevents hallucination and ensures syllabus-only knowledge
        /// </summary>
        private async Task<(bool HasOutsideContent, List<string> OutsideElements)> DetectOutsideSyllabusContentAsync(
            string studentAnswer,
            string syllabusContent,
            CancellationToken cancellationToken)
        {
            try
            {
                var prompt = $@"Analyze if the student's answer contains information NOT present in the syllabus.

Syllabus (authoritative source):
{syllabusContent.Substring(0, Math.Min(2000, syllabusContent.Length))}

Student's Answer:
{studentAnswer}

Identify concepts/facts in student answer that are NOT in syllabus.
Return JSON:
{{
  ""hasOutsideContent"": true/false,
  ""outsideElements"": [""element1"", ""element2""]
}}";

                var response = await _openAiService.GetChatCompletionAsync(prompt);
                if (string.IsNullOrWhiteSpace(response))
                    return (false, new List<string>());

                // Parse JSON response
                var result = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                if (result == null)
                    return (false, new List<string>());

                var hasOutside = result.ContainsKey("hasOutsideContent") &&
                                 Convert.ToBoolean(result["hasOutsideContent"]);

                var elements = new List<string>();
                if (result.ContainsKey("outsideElements") && result["outsideElements"] is Newtonsoft.Json.Linq.JArray arr)
                {
                    elements = arr.ToObject<List<string>>() ?? new();
                }

                return (hasOutside, elements);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to detect outside-syllabus content");
                return (false, new List<string>());
            }
        }

        /// <summary>
        /// Generates feedback restricted to syllabus content
        /// </summary>
        private async Task<string> GenerateSyllabusRestrictedFeedbackAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            string syllabusContent,
            CancellationToken cancellationToken)
        {
            try
            {
                var prompt = $@"You are a {context.Subject} teacher for Class {context.ClassLevel}. Provide brief feedback based ONLY on the syllabus.

Syllabus Content:
{syllabusContent.Substring(0, Math.Min(1500, syllabusContent.Length))}

Question: {context.QuestionText}
Student's Answer: {context.StudentAnswer}
Marks: {result.MarksAwarded}/{result.MaxMarks} (rule-based decision)

Provide 2-3 sentences of feedback:
- ONLY reference concepts from the syllabus
- Do NOT introduce outside knowledge
- Do NOT recalculate marks";

                var feedback = await _openAiService.GetChatCompletionAsync(prompt);
                return feedback ?? "Review the syllabus content for this topic.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate syllabus feedback");
                return result.Improvements.Any()
                    ? string.Join(" ", result.Improvements)
                    : "Please refer to your textbook and syllabus.";
            }
        }
    }
}
