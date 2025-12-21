using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MathNet.Numerics;
using MathNet.Symbolics;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// Mathematics evaluation engine using MathNet.Symbolics
    /// CRITICAL: This engine decides marks, NOT OpenAI
    /// Handles: Numerical, Formula, Derivation questions
    /// </summary>
    public class MathematicsEvaluationEngine : IEvaluationEngine
    {
        private readonly ILogger<MathematicsEvaluationEngine> _logger;
        private readonly OpenAiService _openAiService;

        public string EngineName => "Mathematics Rule-Based Engine";

        // OCR symbol normalization map
        private static readonly Dictionary<string, string> SymbolNormalization = new()
        {
            ["×"] = "*", ["÷"] = "/", ["−"] = "-", ["√"] = "sqrt",
            ["π"] = "pi", ["∞"] = "infinity", ["≈"] = "=",
            ["½"] = "0.5", ["¼"] = "0.25", ["¾"] = "0.75",
            ["²"] = "^2", ["³"] = "^3", ["⁴"] = "^4",
            ["∫"] = "integrate", ["∑"] = "sum", ["∏"] = "product",
            ["∂"] = "d", // partial derivative
        };

        // Common variable synonyms (base=b, height=h, etc.)
        private static readonly Dictionary<string, List<string>> VariableSynonyms = new()
        {
            ["base"] = new() { "b", "base", "l" },
            ["height"] = new() { "h", "height", "alt", "altitude" },
            ["length"] = new() { "l", "length", "len" },
            ["breadth"] = new() { "b", "breadth", "width", "w" },
            ["radius"] = new() { "r", "radius", "rad" },
            ["diameter"] = new() { "d", "diameter", "diam" },
            ["area"] = new() { "A", "area", "a" },
            ["perimeter"] = new() { "P", "perimeter", "p" },
            ["volume"] = new() { "V", "volume", "vol" },
        };

        public MathematicsEvaluationEngine(
            ILogger<MathematicsEvaluationEngine> logger,
            OpenAiService openAiService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
        }

        public bool CanHandle(SubjectCategory subject, QuestionType questionType)
        {
            return subject == SubjectCategory.Mathematics &&
                   (questionType == QuestionType.Numerical ||
                    questionType == QuestionType.Formula ||
                    questionType == QuestionType.Derivation ||
                    questionType == QuestionType.ShortAnswer ||
                    questionType == QuestionType.LongAnswer);
        }

        public async Task<EvaluationEngineResult> EvaluateAsync(
            EvaluationContext context,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Mathematics engine evaluating {QuestionType} question: {QuestionId}",
                context.Type, context.QuestionId);

            var result = new EvaluationEngineResult
            {
                MaxMarks = context.MaxMarks,
                ProcessedBy = EngineName,
                AuditTrail = new Dictionary<string, object>
                {
                    ["OriginalStudentAnswer"] = context.StudentAnswer,
                    ["OriginalModelAnswer"] = context.ModelAnswer,
                    ["EvaluationTimestamp"] = DateTime.UtcNow
                }
            };

            try
            {
                switch (context.Type)
                {
                    case QuestionType.Numerical:
                        await EvaluateNumericalAsync(context, result, cancellationToken);
                        break;

                    case QuestionType.Formula:
                    case QuestionType.Derivation:
                        await EvaluateFormulaOrDerivationAsync(context, result, cancellationToken);
                        break;

                    case QuestionType.ShortAnswer:
                    case QuestionType.LongAnswer:
                        await EvaluateMathConceptualAsync(context, result, cancellationToken);
                        break;

                    default:
                        result.NeedsReview = true;
                        result.EvaluationReason = $"Unsupported question type: {context.Type}";
                        result.ConfidenceScore = 0.3;
                        break;
                }

                // Use OpenAI ONLY for feedback generation, NOT marks
                if (result.MarksAwarded < result.MaxMarks)
                {
                    result.StudentFeedback = await GenerateFeedbackAsync(context, result, cancellationToken);
                }
                else
                {
                    result.StudentFeedback = "Excellent! Your answer is correct and complete.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mathematics evaluation failed for {QuestionId}", context.QuestionId);
                result.NeedsReview = true;
                result.ConfidenceScore = 0;
                result.EvaluationReason = $"Evaluation error: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Evaluates numerical answers (direct number comparison)
        /// </summary>
        private Task EvaluateNumericalAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            var studentNum = ExtractNumericalValue(context.StudentAnswer);
            var modelNum = ExtractNumericalValue(context.ModelAnswer);

            result.AuditTrail["ExtractedStudentNumber"] = studentNum;
            result.AuditTrail["ExtractedModelNumber"] = modelNum;

            if (studentNum.HasValue && modelNum.HasValue)
            {
                // Allow 0.01% tolerance for floating-point comparison
                var tolerance = Math.Abs(modelNum.Value) * 0.0001;
                var difference = Math.Abs(studentNum.Value - modelNum.Value);

                if (difference <= tolerance)
                {
                    result.MarksAwarded = context.MaxMarks;
                    result.ConfidenceScore = 1.0;
                    result.EvaluationReason = $"Numerical match: Student={studentNum:F4}, Model={modelNum:F4}, Diff={difference:E2}";
                    result.Strengths.Add("Correct numerical answer");
                }
                else if (difference <= tolerance * 10) // Within 0.1% - partial credit
                {
                    result.MarksAwarded = context.MaxMarks * 0.5;
                    result.ConfidenceScore = 0.8;
                    result.EvaluationReason = $"Close numerical match (0.1% tolerance): Student={studentNum:F4}, Model={modelNum:F4}, Diff={difference:E2}";
                    result.Improvements.Add("Check your calculation precision");
                    result.NeedsReview = true;
                }
                else
                {
                    result.MarksAwarded = 0;
                    result.ConfidenceScore = 0.9;
                    result.EvaluationReason = $"Incorrect numerical answer: Student={studentNum:F4}, Expected={modelNum:F4}, Diff={difference:F4}";
                    result.Improvements.Add("Review your calculation steps");
                }
            }
            else
            {
                result.MarksAwarded = 0;
                result.ConfidenceScore = 0.5;
                result.EvaluationReason = "Could not extract numerical value from answer";
                result.NeedsReview = true;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Evaluates formula/derivation questions using symbolic equivalence
        /// </summary>
        private Task EvaluateFormulaOrDerivationAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            // Normalize OCR artifacts
            var studentNormalized = NormalizeExpression(context.StudentAnswer);
            var modelNormalized = NormalizeExpression(context.ModelAnswer);

            result.AuditTrail["NormalizedStudentAnswer"] = studentNormalized;
            result.AuditTrail["NormalizedModelAnswer"] = modelNormalized;

            // Check symbolic equivalence using MathNet
            var equivalence = CheckSymbolicEquivalence(studentNormalized, modelNormalized);
            result.AuditTrail["EquivalenceCheck"] = equivalence;

            if (equivalence.IsEquivalent)
            {
                result.MarksAwarded = context.MaxMarks;
                result.ConfidenceScore = equivalence.Confidence;
                result.EvaluationReason = $"Symbolic equivalence confirmed: {equivalence.Explanation}";
                result.Strengths.Add("Mathematically correct formula/expression");
            }
            else
            {
                // Check for partial credit based on steps
                var stepMarks = EvaluateStepWise(context, studentNormalized, modelNormalized);
                result.StepWiseBreakdown = stepMarks;
                result.MarksAwarded = stepMarks.Sum(s => s.MarksAwarded);
                result.ConfidenceScore = stepMarks.Any() ? 0.7 : 0.5;

                if (result.MarksAwarded > 0)
                {
                    result.EvaluationReason = $"Partial credit for correct steps: {result.MarksAwarded}/{context.MaxMarks}";
                    result.Strengths.Add("Some steps are correct");
                    result.Improvements.Add("Review the final formula derivation");
                }
                else
                {
                    result.EvaluationReason = "Formula does not match expected answer";
                    result.Improvements.Add("Review the formula and derivation steps");
                }

                result.NeedsReview = true;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Evaluates conceptual math questions (definitions, explanations)
        /// </summary>
        private Task EvaluateMathConceptualAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            // Keyword-based evaluation for conceptual questions
            var studentLower = context.StudentAnswer.ToLowerInvariant();
            var modelLower = context.ModelAnswer.ToLowerInvariant();

            var matchedKeywords = new List<string>();
            var missingKeywords = new List<string>();

            foreach (var keyword in context.Keywords)
            {
                if (studentLower.Contains(keyword.ToLowerInvariant()))
                {
                    matchedKeywords.Add(keyword);
                }
                else
                {
                    missingKeywords.Add(keyword);
                }
            }

            result.MatchedKeywords = matchedKeywords;
            result.MissingKeywords = missingKeywords;

            var keywordCoverage = context.Keywords.Count > 0
                ? (double)matchedKeywords.Count / context.Keywords.Count
                : 0.5;

            result.MarksAwarded = context.MaxMarks * keywordCoverage;
            result.ConfidenceScore = 0.7;
            result.EvaluationReason = $"Keyword coverage: {matchedKeywords.Count}/{context.Keywords.Count} ({keywordCoverage:P0})";

            if (keywordCoverage >= 0.8)
            {
                result.Strengths.Add("Good understanding of key concepts");
            }
            else if (keywordCoverage >= 0.5)
            {
                result.Strengths.Add("Partial understanding demonstrated");
                result.Improvements.Add("Include more key mathematical terms");
                result.NeedsReview = true;
            }
            else
            {
                result.Improvements.Add("Review the core mathematical concepts");
                result.NeedsReview = true;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Normalizes mathematical expressions (OCR cleanup + standardization)
        /// </summary>
        private string NormalizeExpression(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return string.Empty;

            var normalized = expr;

            // Apply symbol normalization
            foreach (var (symbol, replacement) in SymbolNormalization)
            {
                normalized = normalized.Replace(symbol, replacement);
            }

            // Remove spaces around operators
            normalized = Regex.Replace(normalized, @"\s*([+\-*/=^()])\s*", "$1");

            // Standardize variable names using synonyms
            foreach (var (canonical, synonyms) in VariableSynonyms)
            {
                foreach (var synonym in synonyms)
                {
                    normalized = Regex.Replace(
                        normalized,
                        @"\b" + Regex.Escape(synonym) + @"\b",
                        canonical,
                        RegexOptions.IgnoreCase);
                }
            }

            return normalized.Trim();
        }

        /// <summary>
        /// Checks symbolic equivalence using MathNet.Symbolics
        /// </summary>
        private MathEquivalenceResult CheckSymbolicEquivalence(string studentExpr, string modelExpr)
        {
            var result = new MathEquivalenceResult
            {
                NormalizedStudent = studentExpr,
                NormalizedModel = modelExpr
            };

            try
            {
                // Parse expressions
                var studentParsed = Infix.ParseOrThrow(studentExpr);
                var modelParsed = Infix.ParseOrThrow(modelExpr);

                // Simplify both
                var studentSimplified = Algebraic.Expand(studentParsed);
                var modelSimplified = Algebraic.Expand(modelParsed);

                result.TransformationsApplied.Add($"Student expanded: {Infix.Format(studentSimplified)}");
                result.TransformationsApplied.Add($"Model expanded: {Infix.Format(modelSimplified)}");

                // Check structural equality
                if (studentSimplified.Equals(modelSimplified))
                {
                    result.IsEquivalent = true;
                    result.Confidence = 1.0;
                    result.Explanation = "Expressions are symbolically equivalent after expansion";
                }
                else
                {
                    // Try rearrangement - subtract by expanding (student - model)
                    var diff = Algebraic.Expand(studentSimplified - modelSimplified);
                    if (diff.Equals(Expression.Zero))
                    {
                        result.IsEquivalent = true;
                        result.Confidence = 0.95;
                        result.Explanation = "Expressions are equivalent (difference is zero)";
                    }
                    else
                    {
                        result.IsEquivalent = false;
                        result.Confidence = 0.8;
                        result.Explanation = $"Expressions differ: {Infix.Format(diff)}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Symbolic equivalence check failed, using string comparison");
                result.IsEquivalent = studentExpr.Equals(modelExpr, StringComparison.OrdinalIgnoreCase);
                result.Confidence = 0.6;
                result.Explanation = $"Fallback to string comparison: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Evaluates step-wise for partial credit
        /// </summary>
        private List<StepWiseMarks> EvaluateStepWise(
            EvaluationContext context,
            string studentAnswer,
            string modelAnswer)
        {
            var steps = new List<StepWiseMarks>();

            // Split answers by lines to identify steps
            var studentLines = studentAnswer.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var modelLines = modelAnswer.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            var marksPerStep = context.MaxMarks / Math.Max(modelLines.Length, 1);

            for (int i = 0; i < Math.Min(studentLines.Length, modelLines.Length); i++)
            {
                var studentLine = NormalizeExpression(studentLines[i]);
                var modelLine = NormalizeExpression(modelLines[i]);

                var stepEquiv = CheckSymbolicEquivalence(studentLine, modelLine);

                var step = new StepWiseMarks
                {
                    StepNumber = i + 1,
                    StepDescription = $"Step {i + 1}",
                    MaxMarks = marksPerStep,
                    MarksAwarded = stepEquiv.IsEquivalent ? marksPerStep : 0,
                    Status = stepEquiv.IsEquivalent ? "Complete" : "Incorrect",
                    Feedback = stepEquiv.Explanation
                };

                steps.Add(step);
            }

            return steps;
        }

        /// <summary>
        /// Extracts numerical value from text (handles various formats)
        /// </summary>
        private double? ExtractNumericalValue(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Try direct parse
            var cleaned = Regex.Replace(text, @"[^\d\.\-+eE]", "");
            if (double.TryParse(cleaned, out var value))
            {
                return value;
            }

            // Try extracting first number
            var match = Regex.Match(text, @"-?\d+\.?\d*([eE][+-]?\d+)?");
            if (match.Success && double.TryParse(match.Value, out var extracted))
            {
                return extracted;
            }

            return null;
        }

        /// <summary>
        /// Generates student feedback using OpenAI (NOT for marks!)
        /// </summary>
        private async Task<string> GenerateFeedbackAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                var prompt = $@"You are a mathematics tutor. Provide constructive feedback for a student's answer.

Question: {context.QuestionText}
Expected Answer: {context.ModelAnswer}
Student's Answer: {context.StudentAnswer}
Marks Awarded: {result.MarksAwarded}/{result.MaxMarks} (already decided by rule-based engine)

Provide brief, encouraging feedback (2-3 sentences):
- If partially correct, acknowledge what's right
- Suggest specific improvement
- Do NOT recalculate marks";

                var feedback = await _openAiService.GetChatCompletionAsync(prompt);
                return feedback ?? "Review your solution and try again.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate OpenAI feedback, using default");
                return result.Improvements.Any()
                    ? string.Join(" ", result.Improvements)
                    : "Please review the solution carefully.";
            }
        }
    }
}
