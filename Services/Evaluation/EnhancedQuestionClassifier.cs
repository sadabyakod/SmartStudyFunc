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
    /// PRODUCTION-GRADE: Enhanced question classifier with confidence scoring
    /// Multi-signal classification: keywords + patterns + context
    /// </summary>
    public class EnhancedQuestionClassifier : IQuestionClassifier
    {
        private readonly ILogger<EnhancedQuestionClassifier> _logger;

        // Enhanced subject detection with weighted keywords
        private static readonly Dictionary<SubjectCategory, Dictionary<string, double>> SubjectKeywordsWeighted = new()
        {
            [SubjectCategory.Mathematics] = new()
            {
                ["equation"] = 1.0, ["solve"] = 0.9, ["calculate"] = 0.9, ["prove"] = 0.8,
                ["derivative"] = 1.0, ["integral"] = 1.0, ["algebra"] = 1.0,
                ["geometry"] = 1.0, ["trigonometry"] = 1.0, ["theorem"] = 0.8,
                ["area"] = 0.6, ["perimeter"] = 0.7, ["volume"] = 0.6,
                ["polynomial"] = 1.0, ["quadratic"] = 1.0, ["linear"] = 0.7,
                ["factor"] = 0.7, ["simplify"] = 0.8, ["expand"] = 0.7,
                ["π"] = 0.9, ["sin"] = 0.9, ["cos"] = 0.9, ["tan"] = 0.9,
                ["sqrt"] = 0.8, ["square root"] = 0.8, ["cube"] = 0.6
            },
            [SubjectCategory.Physics] = new()
            {
                ["force"] = 1.0, ["velocity"] = 1.0, ["acceleration"] = 1.0,
                ["newton"] = 1.0, ["energy"] = 0.8, ["power"] = 0.7,
                ["momentum"] = 1.0, ["friction"] = 0.9, ["gravity"] = 0.9,
                ["electric"] = 0.9, ["magnetic"] = 0.9, ["current"] = 0.8,
                ["voltage"] = 1.0, ["resistance"] = 0.9, ["circuit"] = 0.9,
                ["wave"] = 0.7, ["frequency"] = 0.8, ["wavelength"] = 0.9,
                ["optics"] = 1.0, ["lens"] = 0.9, ["mirror"] = 0.8,
                ["thermodynamics"] = 1.0, ["pressure"] = 0.8, ["density"] = 0.8,
                ["joule"] = 1.0, ["watt"] = 1.0, ["ohm"] = 1.0
            },
            [SubjectCategory.Chemistry] = new()
            {
                ["molecule"] = 1.0, ["atom"] = 0.9, ["element"] = 0.8,
                ["compound"] = 0.9, ["reaction"] = 0.9, ["chemical"] = 0.8,
                ["acid"] = 0.9, ["base"] = 0.8, ["pH"] = 1.0,
                ["oxidation"] = 1.0, ["reduction"] = 1.0, ["catalyst"] = 1.0,
                ["mole"] = 1.0, ["molarity"] = 1.0, ["solution"] = 0.7,
                ["periodic table"] = 1.0, ["valency"] = 1.0, ["bond"] = 0.8,
                ["ionic"] = 1.0, ["covalent"] = 1.0, ["organic"] = 0.9,
                ["carbon"] = 0.6, ["hydrogen"] = 0.6, ["oxygen"] = 0.5
            },
            [SubjectCategory.Biology] = new()
            {
                ["cell"] = 0.9, ["organism"] = 0.9, ["photosynthesis"] = 1.0,
                ["respiration"] = 1.0, ["DNA"] = 1.0, ["gene"] = 1.0,
                ["chromosome"] = 1.0, ["evolution"] = 1.0, ["ecosystem"] = 1.0,
                ["species"] = 0.8, ["tissue"] = 0.9, ["organ"] = 0.7,
                ["reproduction"] = 0.9, ["heredity"] = 1.0, ["mitosis"] = 1.0,
                ["meiosis"] = 1.0, ["protein"] = 0.8, ["enzyme"] = 0.9,
                ["digestive"] = 0.9, ["circulatory"] = 1.0, ["nervous"] = 0.9
            },
            [SubjectCategory.SocialScience] = new()
            {
                ["history"] = 0.9, ["geography"] = 0.9, ["civics"] = 1.0,
                ["democracy"] = 1.0, ["constitution"] = 1.0, ["government"] = 0.9,
                ["economy"] = 0.9, ["culture"] = 0.7, ["society"] = 0.7,
                ["latitude"] = 1.0, ["longitude"] = 1.0, ["climate"] = 0.8,
                ["independence"] = 0.9, ["freedom"] = 0.8, ["rights"] = 0.8,
                ["fundamental rights"] = 1.0, ["amendment"] = 1.0
            }
        };

        // Question type detection patterns with regex
        private static readonly Dictionary<QuestionType, List<(Regex Pattern, double Weight)>> TypePatternRegex = new()
        {
            [QuestionType.Numerical] = new()
            {
                (new Regex(@"\b(find|calculate|compute|determine)\s+(the\s+)?(value|number|answer)", RegexOptions.IgnoreCase), 1.0),
                (new Regex(@"\bhow\s+(much|many)\b", RegexOptions.IgnoreCase), 0.9),
                (new Regex(@"=\s*\?|\?\s*$", RegexOptions.IgnoreCase), 0.8),
                (new Regex(@"\bsolve\s+for\b", RegexOptions.IgnoreCase), 0.8),
                (new Regex(@"\d+(\.\d+)?\s*[+\-*/]\s*\d+", RegexOptions.IgnoreCase), 0.7)
            },
            [QuestionType.Formula] = new()
            {
                (new Regex(@"\b(derive|deduce|obtain|establish)\b", RegexOptions.IgnoreCase), 1.0),
                (new Regex(@"\bprove\s+(that|the)\b", RegexOptions.IgnoreCase), 0.9),
                (new Regex(@"\bshow\s+that\b", RegexOptions.IgnoreCase), 0.9),
                (new Regex(@"\b(formula|expression|relation|equation)\b", RegexOptions.IgnoreCase), 0.8)
            },
            [QuestionType.Definition] = new()
            {
                (new Regex(@"\b(define|what\s+is|meaning\s+of)\b", RegexOptions.IgnoreCase), 1.0),
                (new Regex(@"\bstate\s+the\s+(definition|meaning)\b", RegexOptions.IgnoreCase), 0.9),
                (new Regex(@"\bexplain\s+the\s+term\b", RegexOptions.IgnoreCase), 0.8)
            },
            [QuestionType.ShortAnswer] = new()
            {
                (new Regex(@"\b(briefly|in\s+short|2-3\s+lines)\b", RegexOptions.IgnoreCase), 1.0),
                (new Regex(@"\b(state|mention|list|name)\b", RegexOptions.IgnoreCase), 0.7),
                (new Regex(@"\bwrite\s+short\s+note\b", RegexOptions.IgnoreCase), 0.9)
            },
            [QuestionType.LongAnswer] = new()
            {
                (new Regex(@"\b(explain|describe|discuss|elaborate)\b", RegexOptions.IgnoreCase), 0.8),
                (new Regex(@"\b(analyze|compare|contrast|evaluate)\b", RegexOptions.IgnoreCase), 0.9),
                (new Regex(@"\bin\s+detail\b", RegexOptions.IgnoreCase), 1.0)
            },
            [QuestionType.Essay] = new()
            {
                (new Regex(@"\bwrite\s+(an\s+)?essay\b", RegexOptions.IgnoreCase), 1.0),
                (new Regex(@"\b(critically\s+)?(analyze|examine|discuss)\b", RegexOptions.IgnoreCase), 0.7),
                (new Regex(@"\b\d+\s+marks\b.*\b(500|1000)\s+words\b", RegexOptions.IgnoreCase), 0.9)
            },
            [QuestionType.Derivation] = new()
            {
                (new Regex(@"\bderive\s+(the\s+)?(formula|expression|equation)\b", RegexOptions.IgnoreCase), 1.0),
                (new Regex(@"\bstep-?by-?step\b", RegexOptions.IgnoreCase), 0.8),
                (new Regex(@"\bproof\b", RegexOptions.IgnoreCase), 0.7)
            }
        };

        public EnhancedQuestionClassifier(ILogger<EnhancedQuestionClassifier> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<QuestionClassification> ClassifyAsync(
            string questionText,
            string contextHints,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Classifying question (enhanced multi-signal)");

            var classification = new QuestionClassification
            {
                ReasoningTrace = "Enhanced Classification Process:\n"
            };

            // Normalize question text
            var normalizedText = questionText.ToLowerInvariant();
            var fullText = $"{normalizedText} {contextHints?.ToLowerInvariant() ?? ""}";

            // STEP 1: Subject Classification (Multi-signal)
            var subjectScores = new Dictionary<SubjectCategory, double>();

            foreach (var (subject, keywords) in SubjectKeywordsWeighted)
            {
                double score = 0;
                int matchCount = 0;

                foreach (var (keyword, weight) in keywords)
                {
                    if (fullText.Contains(keyword))
                    {
                        score += weight;
                        matchCount++;
                    }
                }

                // Normalize by sqrt(matchCount) to reduce over-weighting
                if (matchCount > 0)
                {
                    score = score / Math.Sqrt(matchCount);
                }

                subjectScores[subject] = score;
            }

            // Select top subject
            var topSubject = subjectScores.OrderByDescending(kv => kv.Value).FirstOrDefault();
            classification.Subject = topSubject.Key != default ? topSubject.Key : SubjectCategory.Unknown;
            classification.SubjectConfidence = NormalizeConfidence(topSubject.Value, 5.0); // Normalize to 0-1

            classification.ReasoningTrace += $"- Subject: {classification.Subject} (confidence: {classification.SubjectConfidence:F2})\n";

            // If very low confidence, mark as unknown
            if (classification.SubjectConfidence < 0.3)
            {
                classification.Subject = SubjectCategory.Unknown;
                _logger.LogWarning("Low subject confidence: {Score}", classification.SubjectConfidence);
            }

            // STEP 2: Question Type Classification (Pattern matching)
            var typeScores = new Dictionary<QuestionType, double>();

            foreach (var (type, patterns) in TypePatternRegex)
            {
                double score = 0;

                foreach (var (pattern, weight) in patterns)
                {
                    if (pattern.IsMatch(fullText))
                    {
                        score += weight;
                    }
                }

                typeScores[type] = score;
            }

            // Select top type
            var topType = typeScores.OrderByDescending(kv => kv.Value).FirstOrDefault();
            classification.Type = topType.Key != default ? topType.Key : QuestionType.Unknown;
            classification.TypeConfidence = NormalizeConfidence(topType.Value, 2.0);

            classification.ReasoningTrace += $"- Type: {classification.Type} (confidence: {classification.TypeConfidence:F2})\n";

            // Fallback: If no strong signal, use heuristics
            if (classification.Type == QuestionType.Unknown || classification.TypeConfidence < 0.3)
            {
                classification.Type = FallbackTypeDetection(normalizedText);
                classification.TypeConfidence = 0.5;
                classification.ReasoningTrace += $"- Applied fallback detection: {classification.Type}\n";
            }

            // STEP 3: Cross-validation (Subject-Type consistency)
            var consistencyCheck = ValidateSubjectTypeConsistency(classification.Subject, classification.Type);
            if (!consistencyCheck.IsConsistent)
            {
                classification.SubjectConfidence *= 0.8; // Reduce confidence
                classification.ReasoningTrace += $"- Warning: Subject-Type inconsistency detected\n";
            }

            _logger.LogInformation("Classification complete: {Subject}/{Type} ({SubjConf:F2}/{TypeConf:F2})",
                classification.Subject, classification.Type,
                classification.SubjectConfidence, classification.TypeConfidence);

            return Task.FromResult(classification);
        }

        /// <summary>
        /// Normalize raw scores to 0-1 confidence range
        /// </summary>
        private double NormalizeConfidence(double rawScore, double maxExpected)
        {
            if (rawScore <= 0) return 0;
            
            // Sigmoid-like normalization
            var normalized = Math.Min(rawScore / maxExpected, 1.0);
            
            // Apply threshold boost for strong signals
            if (normalized > 0.7)
            {
                normalized = 0.7 + (normalized - 0.7) * 1.5; // Boost high confidence
                normalized = Math.Min(normalized, 1.0);
            }
            
            return normalized;
        }

        /// <summary>
        /// Fallback type detection using simple heuristics
        /// </summary>
        private QuestionType FallbackTypeDetection(string text)
        {
            // Check for numerical indicators
            if (text.Contains("calculate") || text.Contains("find") || text.Contains("="))
            {
                return QuestionType.Numerical;
            }

            // Check for definition indicators
            if (text.Contains("define") || text.Contains("what is") || text.Contains("meaning"))
            {
                return QuestionType.Definition;
            }

            // Check word count heuristic
            var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (wordCount < 10)
            {
                return QuestionType.ShortAnswer;
            }
            else if (wordCount > 20)
            {
                return QuestionType.LongAnswer;
            }

            return QuestionType.ShortAnswer; // Default
        }

        /// <summary>
        /// Validate that subject and question type are logically consistent
        /// </summary>
        private (bool IsConsistent, string Reason) ValidateSubjectTypeConsistency(
            SubjectCategory subject,
            QuestionType type)
        {
            // Mathematics rarely has essays
            if (subject == SubjectCategory.Mathematics && type == QuestionType.Essay)
            {
                return (false, "Mathematics questions are rarely essays");
            }

            // Social Science rarely has numerical questions
            if (subject == SubjectCategory.SocialScience && type == QuestionType.Numerical)
            {
                return (false, "Social Science rarely has pure numerical questions");
            }

            // Physics/Chemistry commonly have formulas
            if ((subject == SubjectCategory.Physics || subject == SubjectCategory.Chemistry) &&
                type == QuestionType.Formula)
            {
                return (true, "Common combination");
            }

            return (true, "No inconsistency detected");
        }
    }
}
