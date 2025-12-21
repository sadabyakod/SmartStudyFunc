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
    /// Physics and Chemistry evaluation engine
    /// CRITICAL: Rule-based formula and unit validation
    /// OpenAI used ONLY for explanations, NOT marks
    /// </summary>
    public class PhysicsChemistryEvaluationEngine : IEvaluationEngine
    {
        private readonly ILogger<PhysicsChemistryEvaluationEngine> _logger;
        private readonly OpenAiService _openAiService;

        public string EngineName => "Physics/Chemistry Rule-Based Engine";

        // Common Physics formulas (canonical forms)
        private static readonly Dictionary<string, FormulaRule> PhysicsFormulas = new()
        {
            ["force"] = new FormulaRule
            {
                CanonicalForm = "F=m*a",
                AlternativeForms = new() { "F=ma", "force=mass*acceleration", "a=F/m", "m=F/a" },
                Variables = new() { "F", "m", "a" },
                VariableSynonyms = new()
                {
                    ["F"] = new() { "F", "force", "f" },
                    ["m"] = new() { "m", "mass", "M" },
                    ["a"] = new() { "a", "acceleration", "acc" }
                }
            },
            ["kinetic_energy"] = new FormulaRule
            {
                CanonicalForm = "KE=0.5*m*v^2",
                AlternativeForms = new() { "KE=(1/2)*m*v^2", "E=(m*v^2)/2" },
                Variables = new() { "KE", "m", "v" },
                VariableSynonyms = new()
                {
                    ["KE"] = new() { "KE", "E", "energy" },
                    ["m"] = new() { "m", "mass" },
                    ["v"] = new() { "v", "velocity", "speed" }
                }
            },
            ["ohms_law"] = new FormulaRule
            {
                CanonicalForm = "V=I*R",
                AlternativeForms = new() { "V=IR", "I=V/R", "R=V/I" },
                Variables = new() { "V", "I", "R" },
                VariableSynonyms = new()
                {
                    ["V"] = new() { "V", "voltage", "potential" },
                    ["I"] = new() { "I", "current" },
                    ["R"] = new() { "R", "resistance" }
                }
            },
            ["density"] = new FormulaRule
            {
                CanonicalForm = "ρ=m/V",
                AlternativeForms = new() { "density=mass/volume", "ρ=m/V", "d=m/V" },
                Variables = new() { "ρ", "m", "V" }
            }
        };

        // Common Chemistry formulas
        private static readonly Dictionary<string, FormulaRule> ChemistryFormulas = new()
        {
            ["mole_concept"] = new FormulaRule
            {
                CanonicalForm = "n=m/M",
                AlternativeForms = new() { "moles=mass/molar_mass", "n=mass/M" },
                Variables = new() { "n", "m", "M" }
            },
            ["molarity"] = new FormulaRule
            {
                CanonicalForm = "M=n/V",
                AlternativeForms = new() { "molarity=moles/volume", "C=n/V" },
                Variables = new() { "M", "n", "V" }
            },
            ["ideal_gas"] = new FormulaRule
            {
                CanonicalForm = "PV=nRT",
                AlternativeForms = new() { "P*V=n*R*T", "V=nRT/P" },
                Variables = new() { "P", "V", "n", "R", "T" }
            }
        };

        // SI Unit conversions
        private static readonly Dictionary<string, UnitEquivalence> UnitConversions = new()
        {
            ["length"] = new UnitEquivalence
            {
                BaseUnit = "m",
                Conversions = new()
                {
                    ["km"] = 1000, ["cm"] = 0.01, ["mm"] = 0.001,
                    ["m"] = 1, ["meter"] = 1, ["metre"] = 1
                }
            },
            ["mass"] = new UnitEquivalence
            {
                BaseUnit = "kg",
                Conversions = new()
                {
                    ["kg"] = 1, ["g"] = 0.001, ["mg"] = 0.000001,
                    ["kilogram"] = 1, ["gram"] = 0.001
                }
            },
            ["time"] = new UnitEquivalence
            {
                BaseUnit = "s",
                Conversions = new()
                {
                    ["s"] = 1, ["ms"] = 0.001, ["min"] = 60, ["h"] = 3600,
                    ["second"] = 1, ["minute"] = 60, ["hour"] = 3600
                }
            },
            ["force"] = new UnitEquivalence
            {
                BaseUnit = "N",
                Conversions = new() { ["N"] = 1, ["newton"] = 1, ["kN"] = 1000 }
            },
            ["energy"] = new UnitEquivalence
            {
                BaseUnit = "J",
                Conversions = new() { ["J"] = 1, ["joule"] = 1, ["kJ"] = 1000, ["cal"] = 4.184 }
            },
            ["power"] = new UnitEquivalence
            {
                BaseUnit = "W",
                Conversions = new() { ["W"] = 1, ["watt"] = 1, ["kW"] = 1000 }
            },
            ["voltage"] = new UnitEquivalence
            {
                BaseUnit = "V",
                Conversions = new() { ["V"] = 1, ["volt"] = 1, ["kV"] = 1000, ["mV"] = 0.001 }
            },
            ["current"] = new UnitEquivalence
            {
                BaseUnit = "A",
                Conversions = new() { ["A"] = 1, ["ampere"] = 1, ["mA"] = 0.001 }
            }
        };

        public PhysicsChemistryEvaluationEngine(
            ILogger<PhysicsChemistryEvaluationEngine> logger,
            OpenAiService openAiService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
        }

        public bool CanHandle(SubjectCategory subject, QuestionType questionType)
        {
            return (subject == SubjectCategory.Physics || subject == SubjectCategory.Chemistry) &&
                   (questionType == QuestionType.Numerical ||
                    questionType == QuestionType.Formula ||
                    questionType == QuestionType.ShortAnswer ||
                    questionType == QuestionType.LongAnswer);
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
                    ["OriginalStudentAnswer"] = context.StudentAnswer,
                    ["OriginalModelAnswer"] = context.ModelAnswer
                }
            };

            try
            {
                switch (context.Type)
                {
                    case QuestionType.Numerical:
                        await EvaluateNumericalWithUnitsAsync(context, result, cancellationToken);
                        break;

                    case QuestionType.Formula:
                        await EvaluateFormulaAsync(context, result, cancellationToken);
                        break;

                    case QuestionType.ShortAnswer:
                    case QuestionType.LongAnswer:
                        await EvaluateConceptualAsync(context, result, cancellationToken);
                        break;

                    default:
                        result.NeedsReview = true;
                        result.ConfidenceScore = 0.3;
                        break;
                }

                // Generate feedback (OpenAI explanations only, not marks)
                if (result.MarksAwarded < result.MaxMarks * 0.9)
                {
                    result.StudentFeedback = await GenerateFeedbackAsync(context, result, cancellationToken);
                }
                else
                {
                    result.StudentFeedback = "Excellent work! Your answer is scientifically accurate.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Physics/Chemistry evaluation failed for {QuestionId}", context.QuestionId);
                result.NeedsReview = true;
                result.ConfidenceScore = 0;
                result.EvaluationReason = $"Evaluation error: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Evaluates numerical answers with unit validation
        /// </summary>
        private Task EvaluateNumericalWithUnitsAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            var studentValue = ExtractValueAndUnit(context.StudentAnswer);
            var modelValue = ExtractValueAndUnit(context.ModelAnswer);

            result.AuditTrail["StudentValueUnit"] = studentValue;
            result.AuditTrail["ModelValueUnit"] = modelValue;

            if (studentValue.Value.HasValue && modelValue.Value.HasValue)
            {
                // Convert to common units if needed
                var studentConverted = ConvertToBaseUnit(studentValue.Value.Value, studentValue.Unit);
                var modelConverted = ConvertToBaseUnit(modelValue.Value.Value, modelValue.Unit);

                result.AuditTrail["StudentConverted"] = studentConverted;
                result.AuditTrail["ModelConverted"] = modelConverted;

                // Compare values
                var tolerance = Math.Abs(modelConverted) * 0.02; // 2% tolerance for physics/chemistry
                var difference = Math.Abs(studentConverted - modelConverted);

                if (difference <= tolerance)
                {
                    // Check unit correctness
                    if (AreUnitsEquivalent(studentValue.Unit, modelValue.Unit))
                    {
                        result.MarksAwarded = context.MaxMarks;
                        result.ConfidenceScore = 1.0;
                        result.EvaluationReason = $"Correct value with proper units: {studentConverted:F4} (base unit)";
                        result.Strengths.Add("Correct numerical answer with proper units");
                    }
                    else
                    {
                        result.MarksAwarded = context.MaxMarks * 0.8; // Deduct for wrong units
                        result.ConfidenceScore = 0.9;
                        result.EvaluationReason = $"Correct value but incorrect unit: Student={studentValue.Unit}, Expected={modelValue.Unit}";
                        result.Strengths.Add("Correct numerical calculation");
                        result.Improvements.Add($"Use correct units: {modelValue.Unit}");
                    }
                }
                else
                {
                    result.MarksAwarded = 0;
                    result.ConfidenceScore = 0.9;
                    result.EvaluationReason = $"Incorrect value: Student={studentConverted:F4}, Expected={modelConverted:F4}";
                    result.Improvements.Add("Check your calculation and formula application");
                }
            }
            else
            {
                result.MarksAwarded = 0;
                result.ConfidenceScore = 0.5;
                result.EvaluationReason = "Could not extract numerical value and unit";
                result.NeedsReview = true;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Evaluates formula-based questions
        /// </summary>
        private Task EvaluateFormulaAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            var studentNormalized = NormalizeFormula(context.StudentAnswer);
            var modelNormalized = NormalizeFormula(context.ModelAnswer);

            result.AuditTrail["NormalizedStudent"] = studentNormalized;
            result.AuditTrail["NormalizedModel"] = modelNormalized;

            // Check against known formulas
            var allFormulas = context.Subject == SubjectCategory.Physics
                ? PhysicsFormulas
                : ChemistryFormulas;

            var matchedFormula = FindMatchingFormula(studentNormalized, allFormulas);
            var expectedFormula = FindMatchingFormula(modelNormalized, allFormulas);

            if (matchedFormula != null && expectedFormula != null &&
                matchedFormula.CanonicalForm == expectedFormula.CanonicalForm)
            {
                result.MarksAwarded = context.MaxMarks;
                result.ConfidenceScore = 0.95;
                result.EvaluationReason = $"Correct formula: {matchedFormula.CanonicalForm}";
                result.Strengths.Add($"Correctly applied {matchedFormula.Description ?? "formula"}");
            }
            else if (studentNormalized.Equals(modelNormalized, StringComparison.OrdinalIgnoreCase))
            {
                // Exact string match (fallback)
                result.MarksAwarded = context.MaxMarks;
                result.ConfidenceScore = 0.85;
                result.EvaluationReason = "Formula matches (string comparison)";
                result.Strengths.Add("Correct formula");
            }
            else
            {
                // Check for partial credit
                var partialScore = CalculateFormulaPartialCredit(studentNormalized, modelNormalized);
                result.MarksAwarded = context.MaxMarks * partialScore;
                result.ConfidenceScore = 0.7;
                result.EvaluationReason = $"Partial match: {partialScore:P0} similarity";
                result.Improvements.Add("Review the formula derivation and variable relationships");
                result.NeedsReview = partialScore < 0.5;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Evaluates conceptual questions with keyword matching
        /// </summary>
        private Task EvaluateConceptualAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            var studentLower = context.StudentAnswer.ToLowerInvariant();

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
            result.ConfidenceScore = 0.75;
            result.EvaluationReason = $"Keyword coverage: {matchedKeywords.Count}/{context.Keywords.Count} ({keywordCoverage:P0})";

            if (keywordCoverage >= 0.8)
            {
                result.Strengths.Add("Good coverage of scientific concepts");
            }
            else if (keywordCoverage >= 0.5)
            {
                result.Strengths.Add("Partial understanding demonstrated");
                result.Improvements.Add($"Include these key terms: {string.Join(", ", missingKeywords.Take(3))}");
                result.NeedsReview = true;
            }
            else
            {
                result.Improvements.Add("Review the core scientific concepts and terminology");
                result.NeedsReview = true;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Extracts numerical value and unit from text
        /// </summary>
        private (double? Value, string Unit) ExtractValueAndUnit(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (null, string.Empty);

            // Pattern: number followed by optional space and unit
            var match = Regex.Match(text, @"(-?\d+\.?\d*([eE][+-]?\d+)?)\s*([a-zA-Z/^²³]+)?");
            if (match.Success)
            {
                var valueStr = match.Groups[1].Value;
                var unit = match.Groups[3].Value;

                if (double.TryParse(valueStr, out var value))
                {
                    return (value, unit);
                }
            }

            return (null, string.Empty);
        }

        /// <summary>
        /// Converts value to base SI unit
        /// </summary>
        private double ConvertToBaseUnit(double value, string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
                return value;

            foreach (var (_, equivalence) in UnitConversions)
            {
                if (equivalence.Conversions.TryGetValue(unit, out var conversion))
                {
                    return value * conversion;
                }
            }

            return value; // No conversion found, return as-is
        }

        /// <summary>
        /// Checks if two units are equivalent
        /// </summary>
        private bool AreUnitsEquivalent(string unit1, string unit2)
        {
            if (string.IsNullOrWhiteSpace(unit1) || string.IsNullOrWhiteSpace(unit2))
                return true; // Lenient if one unit is missing

            if (unit1.Equals(unit2, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check if both belong to same unit category
            foreach (var (_, equivalence) in UnitConversions)
            {
                var hasUnit1 = equivalence.Conversions.ContainsKey(unit1);
                var hasUnit2 = equivalence.Conversions.ContainsKey(unit2);
                if (hasUnit1 && hasUnit2)
                    return true; // Same category, different scales acceptable
            }

            return false;
        }

        /// <summary>
        /// Normalizes formula string
        /// </summary>
        private string NormalizeFormula(string formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) return string.Empty;

            var normalized = formula
                .Replace(" ", "")
                .Replace("×", "*")
                .Replace("÷", "/")
                .Replace("−", "-")
                .ToLowerInvariant();

            return normalized;
        }

        /// <summary>
        /// Finds matching formula from library
        /// </summary>
        private FormulaRule? FindMatchingFormula(
            string formula,
            Dictionary<string, FormulaRule> formulaLibrary)
        {
            foreach (var (_, rule) in formulaLibrary)
            {
                if (NormalizeFormula(rule.CanonicalForm).Contains(formula) ||
                    formula.Contains(NormalizeFormula(rule.CanonicalForm)))
                {
                    return rule;
                }

                foreach (var altForm in rule.AlternativeForms)
                {
                    if (NormalizeFormula(altForm).Equals(formula, StringComparison.OrdinalIgnoreCase))
                    {
                        return rule;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates partial credit based on formula similarity
        /// </summary>
        private double CalculateFormulaPartialCredit(string student, string model)
        {
            // Simple character-based similarity
            var longer = Math.Max(student.Length, model.Length);
            if (longer == 0) return 0;

            var matchingChars = student.Where((c, i) => i < model.Length && c == model[i]).Count();
            return (double)matchingChars / longer;
        }

        /// <summary>
        /// Generates explanatory feedback (OpenAI - NOT for marks)
        /// </summary>
        private async Task<string> GenerateFeedbackAsync(
            EvaluationContext context,
            EvaluationEngineResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                var prompt = $@"You are a {context.Subject} tutor. Provide brief, helpful feedback for a student's answer.

Question: {context.QuestionText}
Expected Answer: {context.ModelAnswer}
Student's Answer: {context.StudentAnswer}
Marks Awarded: {result.MarksAwarded}/{result.MaxMarks} (rule-based decision - do NOT change)

Provide 2-3 sentences of constructive feedback focusing on conceptual understanding.
Do NOT recalculate marks.";

                var feedback = await _openAiService.GetChatCompletionAsync(prompt);
                return feedback ?? "Review the scientific principles and try again.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate OpenAI feedback");
                return result.Improvements.Any()
                    ? string.Join(" ", result.Improvements)
                    : "Please review the scientific concepts.";
            }
        }
    }
}
