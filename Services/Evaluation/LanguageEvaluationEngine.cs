using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// Language (English, Hindi, Regional) evaluation engine
    /// Uses rubric-based scoring: Grammar, Structure, Relevance, Vocabulary
    /// NO binary right/wrong logic - continuous scoring
    /// </summary>
    public class LanguageEvaluationEngine : IEvaluationEngine
    {
        private readonly ILogger<LanguageEvaluationEngine> _logger;
        private readonly OpenAiService _openAiService;

        public string EngineName => "Language Rubric-Based Engine";

        // Default rubric weights
        private static readonly LanguageRubric DefaultRubric = new()
        {
            GrammarWeight = 0.25,      // 25% - Rule-based grammar checks
            StructureWeight = 0.25,    // 25% - Organization, flow
            RelevanceWeight = 0.30,    // 30% - Addresses the question
            VocabularyWeight = 0.20,   // 20% - Word choice, variety
            RequiredElements = new() { "introduction", "body", "conclusion" }
        };

        // Common grammar patterns (simplified)
        private static readonly Dictionary<string, string> GrammarRules = new()
        {
            [@"\b(a)\s+(aeiou)"] = "Use 'an' before vowels",
            [@"\b(an)\s+([^aeiou])"] = "Use 'a' before consonants",
            [@"(there|their|they're)"] = "Check there/their/they're usage",
            [@"(your|you're)"] = "Check your/you're usage",
            [@"(its|it's)"] = "Check its/it's usage",
            [@"\.\.+"] = "Avoid multiple periods",
            [@"\s{2,}"] = "Avoid multiple spaces",
        };

        public LanguageEvaluationEngine(
            ILogger<LanguageEvaluationEngine> logger,
            OpenAiService openAiService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
        }

        public bool CanHandle(SubjectCategory subject, QuestionType questionType)
        {
            return (subject == SubjectCategory.English ||
                    subject == SubjectCategory.Hindi ||
                    subject == SubjectCategory.RegionalLanguage) &&
                   (questionType == QuestionType.ShortAnswer ||
                    questionType == QuestionType.LongAnswer ||
                    questionType == QuestionType.Essay);
        }

        public async Task<EvaluationEngineResult> EvaluateAsync(
            EvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Language engine evaluating {QuestionType} for {Subject}: {QuestionId}",
                context.Type, context.Subject, context.QuestionId);

            var result = new EvaluationEngineResult
            {
                MaxMarks = context.MaxMarks,
                ProcessedBy = EngineName,
                AuditTrail = new Dictionary<string, object>
                {
                    ["Subject"] = context.Subject.ToString(),
                    ["QuestionType"] = context.Type.ToString(),
                    ["EvaluationTimestamp"] = DateTime.UtcNow
                }
            };

            try
            {
                // Load rubric (could be from Blob config in production)
                var rubric = LoadRubric(context);

                // Step 1: Grammar Score (Rule-based)
                var grammarScore = EvaluateGrammar(context.StudentAnswer);
                result.AuditTrail["GrammarScore"] = grammarScore;

                // Step 2: Structure Score (Rule-based + AI)
                var structureScore = await EvaluateStructureAsync(context, cancellationToken);
                result.AuditTrail["StructureScore"] = structureScore;

                // Step 3: Relevance Score (Semantic matching)
                var relevanceScore = await EvaluateRelevanceAsync(context, cancellationToken);
                result.AuditTrail["RelevanceScore"] = relevanceScore;

                // Step 4: Vocabulary Score (Rule-based)
                var vocabularyScore = EvaluateVocabulary(context.StudentAnswer);
                result.AuditTrail["VocabularyScore"] = vocabularyScore;

                // Step 5: Calculate weighted total
                var totalScore =
                    (grammarScore.Score * rubric.GrammarWeight) +
                    (structureScore.Score * rubric.StructureWeight) +
                    (relevanceScore.Score * rubric.RelevanceWeight) +
                    (vocabularyScore.Score * rubric.VocabularyWeight);

                result.MarksAwarded = context.MaxMarks * totalScore;
                result.ConfidenceScore = 0.8;

                // Step 6: Build evaluation reason
                result.EvaluationReason = $"Rubric-based: Grammar={grammarScore.Score:P0}, " +
                    $"Structure={structureScore.Score:P0}, " +
                    $"Relevance={relevanceScore.Score:P0}, " +
                    $"Vocabulary={vocabularyScore.Score:P0} → Total={totalScore:P0}";

                // Step 7: Create step-wise breakdown
                result.StepWiseBreakdown = new List<StepWiseMarks>
                {
                    new StepWiseMarks
                    {
                        StepNumber = 1,
                        StepDescription = "Grammar & Mechanics",
                        MaxMarks = context.MaxMarks * rubric.GrammarWeight,
                        MarksAwarded = context.MaxMarks * rubric.GrammarWeight * grammarScore.Score,
                        Status = grammarScore.Score >= 0.8 ? "Complete" : "Partial",
                        Feedback = grammarScore.Feedback
                    },
                    new StepWiseMarks
                    {
                        StepNumber = 2,
                        StepDescription = "Structure & Organization",
                        MaxMarks = context.MaxMarks * rubric.StructureWeight,
                        MarksAwarded = context.MaxMarks * rubric.StructureWeight * structureScore.Score,
                        Status = structureScore.Score >= 0.8 ? "Complete" : "Partial",
                        Feedback = structureScore.Feedback
                    },
                    new StepWiseMarks
                    {
                        StepNumber = 3,
                        StepDescription = "Relevance & Content",
                        MaxMarks = context.MaxMarks * rubric.RelevanceWeight,
                        MarksAwarded = context.MaxMarks * rubric.RelevanceWeight * relevanceScore.Score,
                        Status = relevanceScore.Score >= 0.8 ? "Complete" : "Partial",
                        Feedback = relevanceScore.Feedback
                    },
                    new StepWiseMarks
                    {
                        StepNumber = 4,
                        StepDescription = "Vocabulary & Expression",
                        MaxMarks = context.MaxMarks * rubric.VocabularyWeight,
                        MarksAwarded = context.MaxMarks * rubric.VocabularyWeight * vocabularyScore.Score,
                        Status = vocabularyScore.Score >= 0.8 ? "Complete" : "Partial",
                        Feedback = vocabularyScore.Feedback
                    }
                };

                // Step 8: Collect strengths and improvements
                CollectFeedback(result, grammarScore, structureScore, relevanceScore, vocabularyScore);

                // Step 9: Generate holistic feedback
                result.StudentFeedback = await GenerateLanguageFeedbackAsync(
                    context,
                    result,
                    cancellationToken);

                // Flag for review if score is borderline
                result.NeedsReview = totalScore >= 0.4 && totalScore <= 0.6;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Language evaluation failed for {QuestionId}", context.QuestionId);
                result.NeedsReview = true;
                result.ConfidenceScore = 0;
                result.EvaluationReason = $"Evaluation error: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Loads rubric (from config or uses default)
        /// </summary>
        private LanguageRubric LoadRubric(EvaluationContext context)
        {
            // In production, load from Azure Blob based on class level and language
            // For now, use default
            return DefaultRubric;
        }

        /// <summary>
        /// Evaluates grammar using rule-based checks
        /// </summary>
        private (double Score, string Feedback) EvaluateGrammar(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return (0, "No content to evaluate");

            var issues = new List<string>();
            var wordCount = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            // Check sentence structure
            var sentences = answer.Split('.', '!', '?')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (sentences.Count == 0)
            {
                issues.Add("No complete sentences found");
            }

            // Apply grammar rules
            foreach (var (pattern, message) in GrammarRules)
            {
                var matches = Regex.Matches(answer, pattern, RegexOptions.IgnoreCase);
                if (matches.Count > 0)
                {
                    issues.Add($"{message} ({matches.Count}x)");
                }
            }

            // Check capitalization
            if (!char.IsUpper(answer.TrimStart()[0]))
            {
                issues.Add("Start with capital letter");
            }

            // Calculate score
            var score = 1.0 - (Math.Min(issues.Count, 5) * 0.15); // Deduct 15% per issue, max 5
            score = Math.Max(score, 0.5); // Minimum 50% for having content

            var feedback = issues.Any()
                ? $"Grammar issues: {string.Join("; ", issues.Take(3))}"
                : "Good grammar and mechanics";

            return (score, feedback);
        }

        /// <summary>
        /// Evaluates structure and organization
        /// </summary>
        private async Task<(double Score, string Feedback)> EvaluateStructureAsync(
            EvaluationContext context,
            CancellationToken cancellationToken)
        {
            var answer = context.StudentAnswer;
            var paragraphs = answer.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            double score = 0.5; // Base score
            var feedback = new List<string>();

            // For essays, expect multiple paragraphs
            if (context.Type == QuestionType.Essay)
            {
                if (paragraphs.Length >= 3)
                {
                    score += 0.3;
                    feedback.Add("Good paragraph structure");
                }
                else if (paragraphs.Length >= 2)
                {
                    score += 0.15;
                    feedback.Add("Add more paragraphs");
                }
                else
                {
                    feedback.Add("Break into multiple paragraphs");
                }
            }
            else
            {
                score += 0.2; // Short answers don't need multiple paragraphs
            }

            // Check for transitions
            var transitionWords = new[] { "however", "moreover", "furthermore", "therefore", "consequently", "additionally" };
            var hasTransitions = transitionWords.Any(t => answer.ToLowerInvariant().Contains(t));
            if (hasTransitions)
            {
                score += 0.2;
                feedback.Add("Good use of transitions");
            }

            return (Math.Min(score, 1.0), string.Join("; ", feedback.Any() ? feedback : new[] { "Acceptable structure" }));
        }

        /// <summary>
        /// Evaluates relevance to the question
        /// </summary>
        private async Task<(double Score, string Feedback)> EvaluateRelevanceAsync(
            EvaluationContext context,
            CancellationToken cancellationToken)
        {
            // Extract key topic words from question
            var questionWords = context.QuestionText
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 4)
                .Distinct()
                .ToList();

            var answerLower = context.StudentAnswer.ToLowerInvariant();
            var matchedWords = questionWords.Count(w => answerLower.Contains(w));

            var relevanceScore = questionWords.Count > 0
                ? (double)matchedWords / questionWords.Count
                : 0.5;

            // Check if keywords from context are present
            if (context.Keywords.Any())
            {
                var keywordMatches = context.Keywords.Count(k => answerLower.Contains(k.ToLowerInvariant()));
                var keywordScore = (double)keywordMatches / context.Keywords.Count;
                relevanceScore = (relevanceScore + keywordScore) / 2;
            }

            var feedback = relevanceScore >= 0.7
                ? "Stays on topic well"
                : relevanceScore >= 0.5
                ? "Partially addresses the question"
                : "Focus more on the question asked";

            return (relevanceScore, feedback);
        }

        /// <summary>
        /// Evaluates vocabulary richness
        /// </summary>
        private (double Score, string Feedback) EvaluateVocabulary(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return (0, "No content");

            var words = answer.ToLowerInvariant()
                .Split(new[] { ' ', '.', ',', '!', '?', ';', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .ToList();

            if (words.Count == 0)
                return (0.5, "Limited vocabulary");

            var uniqueWords = words.Distinct().Count();
            var diversityRatio = (double)uniqueWords / words.Count;

            // Good vocabulary has 60%+ unique words
            var score = diversityRatio >= 0.6 ? 1.0 :
                        diversityRatio >= 0.4 ? 0.8 :
                        0.6;

            // Check for advanced words (more than 8 letters)
            var advancedWords = words.Count(w => w.Length > 8);
            if (advancedWords >= 3)
            {
                score = Math.Min(score + 0.1, 1.0);
            }

            var feedback = score >= 0.9 ? "Rich and varied vocabulary" :
                          score >= 0.7 ? "Good vocabulary usage" :
                          "Use more varied vocabulary";

            return (score, feedback);
        }

        /// <summary>
        /// Collects feedback from all rubric components
        /// </summary>
        private void CollectFeedback(
            EvaluationEngineResult result,
            (double Score, string Feedback) grammar,
            (double Score, string Feedback) structure,
            (double Score, string Feedback) relevance,
            (double Score, string Feedback) vocabulary)
        {
            // Strengths
            if (grammar.Score >= 0.8)
                result.Strengths.Add("Strong grammar");
            if (structure.Score >= 0.8)
                result.Strengths.Add("Well-organized");
            if (relevance.Score >= 0.8)
                result.Strengths.Add("Addresses the topic well");
            if (vocabulary.Score >= 0.8)
                result.Strengths.Add("Good vocabulary");

            // Improvements
            if (grammar.Score < 0.7)
                result.Improvements.Add(grammar.Feedback);
            if (structure.Score < 0.7)
                result.Improvements.Add(structure.Feedback);
            if (relevance.Score < 0.7)
                result.Improvements.Add(relevance.Feedback);
            if (vocabulary.Score < 0.7)
                result.Improvements.Add(vocabulary.Feedback);
        }

        /// <summary>
        /// Generates holistic language feedback
        /// </summary>
        private async Task<string> GenerateLanguageFeedbackAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                var prompt = $@"You are a language teacher. Provide encouraging feedback for a student's {context.Subject} answer.

Question: {context.QuestionText}
Student's Answer: {context.StudentAnswer}
Marks: {result.MarksAwarded:F1}/{result.MaxMarks} (rubric-based)

Rubric Breakdown:
{string.Join("\n", result.StepWiseBreakdown.Select(s => $"- {s.StepDescription}: {s.MarksAwarded:F1}/{s.MaxMarks:F1}"))}

Provide 2-3 sentences of constructive, encouraging feedback.
Do NOT recalculate marks.";

                var feedback = await _openAiService.GetChatCompletionAsync(prompt);
                return feedback ?? "Keep practicing to improve your writing skills.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate language feedback");
                return result.Strengths.Any()
                    ? $"{string.Join(", ", result.Strengths)}. {string.Join(" ", result.Improvements)}"
                    : "Continue practicing your language skills.";
            }
        }
    }
}
