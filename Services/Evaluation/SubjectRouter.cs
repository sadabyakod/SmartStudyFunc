using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// Routes evaluation requests to appropriate subject-specific engines
    /// Central orchestrator that ensures correct engine selection
    /// </summary>
    public class SubjectRouter : ISubjectRouter
    {
        private readonly ILogger<SubjectRouter> _logger;
        private readonly IQuestionClassifier _classifier;
        private readonly IEnumerable<IEvaluationEngine> _engines;

        public SubjectRouter(
            ILogger<SubjectRouter> logger,
            IQuestionClassifier classifier,
            IEnumerable<IEvaluationEngine> engines)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
            _engines = engines ?? throw new ArgumentNullException(nameof(engines));

            var engineList = _engines.ToList();
            _logger.LogInformation(
                "SubjectRouter initialized with {Count} engines: {Engines}",
                engineList.Count,
                string.Join(", ", engineList.Select(e => e.EngineName)));
        }

        public async Task<EvaluationEngineResult> RouteAndEvaluateAsync(
            EvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Routing evaluation for QuestionId={QuestionId}, Subject={Subject}, Type={Type}",
                context.QuestionId, context.Subject, context.Type);

            try
            {
                // Step 1: Classify if subject/type not already set
                if (context.Subject == SubjectCategory.Unknown || context.Type == QuestionType.Unknown)
                {
                    _logger.LogInformation("Classification needed for question: {QuestionText}",
                        context.QuestionText.Substring(0, Math.Min(100, context.QuestionText.Length)));

                    var classification = await _classifier.ClassifyAsync(
                        context.QuestionText,
                        context.SyllabusReference,
                        cancellationToken);

                    context.Subject = classification.Subject;
                    context.Type = classification.Type;

                    _logger.LogInformation(
                        "Question classified as {Subject} ({SubjectConf:F2}) / {Type} ({TypeConf:F2}): {Trace}",
                        classification.Subject,
                        classification.SubjectConfidence,
                        classification.Type,
                        classification.TypeConfidence,
                        classification.ReasoningTrace);
                }

                // Step 2: Find appropriate engine
                var selectedEngine = SelectEngine(context.Subject, context.Type);

                if (selectedEngine == null)
                {
                    _logger.LogWarning(
                        "No engine found for {Subject}/{Type}, using fallback",
                        context.Subject, context.Type);

                    return CreateFallbackResult(context);
                }

                _logger.LogInformation(
                    "Selected engine: {EngineName} for {Subject}/{Type}",
                    selectedEngine.EngineName, context.Subject, context.Type);

                // Step 3: Execute evaluation
                var result = await selectedEngine.EvaluateAsync(context, cancellationToken);

                _logger.LogInformation(
                    "Evaluation complete: {MarksAwarded}/{MaxMarks} (Confidence={Confidence:F2}, NeedsReview={NeedsReview})",
                    result.MarksAwarded, result.MaxMarks, result.ConfidenceScore, result.NeedsReview);

                // Step 4: Validate result
                ValidateResult(result, context);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Routing and evaluation failed for {QuestionId}", context.QuestionId);

                return new EvaluationEngineResult
                {
                    MarksAwarded = 0,
                    MaxMarks = context.MaxMarks,
                    ConfidenceScore = 0,
                    NeedsReview = true,
                    EvaluationReason = $"Evaluation failed: {ex.Message}",
                    ProcessedBy = "SubjectRouter (Error Handler)",
                    StudentFeedback = "Unable to evaluate automatically. Teacher review required.",
                    AuditTrail = new Dictionary<string, object>
                    {
                        ["ErrorType"] = ex.GetType().Name,
                        ["ErrorMessage"] = ex.Message,
                        ["Timestamp"] = DateTime.UtcNow
                    }
                };
            }
        }

        /// <summary>
        /// Selects the appropriate evaluation engine based on subject and question type
        /// </summary>
        private IEvaluationEngine? SelectEngine(SubjectCategory subject, QuestionType questionType)
        {
            // Try to find an engine that can handle this subject/type combination
            var matchingEngine = _engines.FirstOrDefault(e => e.CanHandle(subject, questionType));

            if (matchingEngine != null)
            {
                return matchingEngine;
            }

            // Fallback: Try to find any engine for this subject
            var subjectEngine = _engines.FirstOrDefault(e =>
                e.CanHandle(subject, QuestionType.ShortAnswer) ||
                e.CanHandle(subject, QuestionType.LongAnswer));

            return subjectEngine;
        }

        /// <summary>
        /// Creates a fallback result when no suitable engine is found
        /// Uses basic keyword matching
        /// </summary>
        private EvaluationEngineResult CreateFallbackResult(EvaluationContext context)
        {
            var result = new EvaluationEngineResult
            {
                MaxMarks = context.MaxMarks,
                ProcessedBy = "Fallback Keyword Matcher",
                ConfidenceScore = 0.5,
                NeedsReview = true,
                AuditTrail = new Dictionary<string, object>
                {
                    ["FallbackReason"] = $"No engine found for {context.Subject}/{context.Type}",
                    ["Timestamp"] = DateTime.UtcNow
                }
            };

            // Simple keyword-based scoring
            var studentLower = context.StudentAnswer.ToLowerInvariant();
            var matchedKeywords = context.Keywords
                .Where(k => studentLower.Contains(k.ToLowerInvariant()))
                .ToList();

            result.MatchedKeywords = matchedKeywords;
            result.MissingKeywords = context.Keywords.Except(matchedKeywords).ToList();

            var keywordScore = context.Keywords.Any()
                ? (double)matchedKeywords.Count / context.Keywords.Count
                : 0.5;

            result.MarksAwarded = context.MaxMarks * keywordScore;
            result.EvaluationReason = $"Fallback keyword matching: {matchedKeywords.Count}/{context.Keywords.Count} keywords";

            if (keywordScore >= 0.7)
            {
                result.Strengths.Add("Key terms present");
            }
            else
            {
                result.Improvements.Add("Include more relevant keywords");
            }

            result.StudentFeedback = "This answer requires manual teacher review for accurate grading.";

            _logger.LogWarning(
                "Fallback evaluation used for {QuestionId}: {Score}/{MaxMarks}",
                context.QuestionId, result.MarksAwarded, result.MaxMarks);

            return result;
        }

        /// <summary>
        /// Validates evaluation result for consistency
        /// </summary>
        private void ValidateResult(EvaluationEngineResult result, EvaluationContext context)
        {
            // Ensure marks are within bounds
            if (result.MarksAwarded < 0)
            {
                _logger.LogWarning("Negative marks detected, clamping to 0");
                result.MarksAwarded = 0;
            }

            if (result.MarksAwarded > result.MaxMarks)
            {
                _logger.LogWarning(
                    "Marks exceed maximum ({Awarded} > {Max}), clamping",
                    result.MarksAwarded, result.MaxMarks);
                result.MarksAwarded = result.MaxMarks;
            }

            // Ensure confidence is in valid range
            if (result.ConfidenceScore < 0 || result.ConfidenceScore > 1)
            {
                _logger.LogWarning(
                    "Invalid confidence score {Score}, clamping to [0, 1]",
                    result.ConfidenceScore);
                result.ConfidenceScore = Math.Clamp(result.ConfidenceScore, 0, 1);
            }

            // Auto-flag for review if confidence is low
            if (result.ConfidenceScore < 0.6 && !result.NeedsReview)
            {
                _logger.LogInformation(
                    "Low confidence ({Score:F2}), flagging for review",
                    result.ConfidenceScore);
                result.NeedsReview = true;
            }

            // Ensure audit trail has minimum required fields
            if (!result.AuditTrail.ContainsKey("EvaluationTimestamp"))
            {
                result.AuditTrail["EvaluationTimestamp"] = DateTime.UtcNow;
            }

            result.AuditTrail["Subject"] = context.Subject.ToString();
            result.AuditTrail["QuestionType"] = context.Type.ToString();
            result.AuditTrail["QuestionId"] = context.QuestionId;
        }
    }
}
