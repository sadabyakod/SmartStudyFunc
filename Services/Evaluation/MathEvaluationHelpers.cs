using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MathNet.Symbolics;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// PRODUCTION-GRADE: Mathematics evaluation helper methods
    /// Handles OCR normalization, variable aliasing, unit extraction
    /// </summary>
    public static class MathEvaluationHelpers
    {
        // Extended OCR normalization (comprehensive)
        private static readonly Dictionary<string, string> ExtendedSymbolMap = new()
        {
            // Multiplication
            ["×"] = "*", ["·"] = "*", ["∗"] = "*", ["⋅"] = "*",
            // Division
            ["÷"] = "/", ["∕"] = "/",
            // Subtraction
            ["−"] = "-", ["–"] = "-", ["—"] = "-",
            // Math symbols
            ["√"] = "sqrt", ["∛"] = "cbrt", ["π"] = "pi", ["∞"] = "infinity",
            ["≈"] = "=", ["≡"] = "=", ["≅"] = "=",
            // Fractions (common OCR errors)
            ["½"] = "(1/2)", ["⅓"] = "(1/3)", ["¼"] = "(1/4)", ["¾"] = "(3/4)",
            ["⅕"] = "(1/5)", ["⅙"] = "(1/6)", ["⅛"] = "(1/8)", ["⅔"] = "(2/3)",
            // Exponents
            ["²"] = "^2", ["³"] = "^3", ["⁴"] = "^4", ["⁵"] = "^5",
            ["⁶"] = "^6", ["⁷"] = "^7", ["⁸"] = "^8", ["⁹"] = "^9",
            ["⁰"] = "^0", ["¹"] = "^1",
            // Calculus
            ["∫"] = "integrate", ["∑"] = "sum", ["∏"] = "product",
            ["∂"] = "d", ["∆"] = "delta", ["Δ"] = "delta",
            // Greek letters (common in physics/math)
            ["α"] = "alpha", ["β"] = "beta", ["γ"] = "gamma", ["δ"] = "delta",
            ["θ"] = "theta", ["λ"] = "lambda", ["μ"] = "mu", ["ρ"] = "rho",
            ["σ"] = "sigma", ["φ"] = "phi", ["ω"] = "omega"
        };

        // Enhanced variable synonym mapping (geometry, algebra, physics)
        private static readonly Dictionary<string, List<string>> EnhancedVariableSynonyms = new()
        {
            // Geometry
            ["base"] = new() { "b", "base", "B", "l", "length" },
            ["height"] = new() { "h", "height", "H", "alt", "altitude" },
            ["length"] = new() { "l", "length", "L", "len" },
            ["breadth"] = new() { "b", "breadth", "B", "width", "w", "W" },
            ["width"] = new() { "w", "width", "W", "b", "breadth" },
            ["radius"] = new() { "r", "radius", "R", "rad" },
            ["diameter"] = new() { "d", "diameter", "D", "diam" },
            ["area"] = new() { "A", "area", "a" },
            ["perimeter"] = new() { "P", "perimeter", "p", "perim" },
            ["volume"] = new() { "V", "volume", "v", "vol" },
            ["side"] = new() { "s", "side", "S", "a" },
            
            // Algebra
            ["x"] = new() { "x", "X" },
            ["y"] = new() { "y", "Y" },
            ["z"] = new() { "z", "Z" },
            ["n"] = new() { "n", "N" },
            
            // Physics
            ["mass"] = new() { "m", "mass", "M" },
            ["time"] = new() { "t", "time", "T" },
            ["distance"] = new() { "d", "distance", "D", "s", "displacement" },
            ["velocity"] = new() { "v", "velocity", "V", "vel", "speed" },
            ["acceleration"] = new() { "a", "acceleration", "A", "acc" },
            ["force"] = new() { "F", "force", "f" }
        };

        /// <summary>
        /// Normalize expression for symbolic comparison
        /// Handles OCR artifacts, whitespace, case variations
        /// </summary>
        public static string NormalizeExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return string.Empty;

            var normalized = expression.Trim();

            // Step 1: Replace extended symbols
            foreach (var (symbol, replacement) in ExtendedSymbolMap)
            {
                normalized = normalized.Replace(symbol, replacement);
            }

            // Step 2: Normalize whitespace around operators
            normalized = Regex.Replace(normalized, @"\s*([+\-*/=^()])\s*", "$1");

            // Step 3: Handle implicit multiplication (2x -> 2*x)
            normalized = Regex.Replace(normalized, @"(\d)([a-zA-Z])", "$1*$2");
            normalized = Regex.Replace(normalized, @"([a-zA-Z])(\d)", "$1*$2");

            // Step 4: Handle power notation (x2 -> x^2 if not already handled)
            normalized = Regex.Replace(normalized, @"([a-zA-Z])(\d+)(?!\^)", "$1^$2");

            // Step 5: Remove extra spaces
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            return normalized;
        }

        /// <summary>
        /// Apply variable aliasing (e.g., base->b, height->h)
        /// Returns list of possible canonical forms
        /// </summary>
        public static List<string> GenerateVariableAliases(string expression)
        {
            var aliases = new List<string> { expression };

            // Find all words that might be variable synonyms
            var words = Regex.Matches(expression, @"\b[a-zA-Z]+\b")
                             .Cast<Match>()
                             .Select(m => m.Value.ToLower())
                             .Distinct()
                             .ToList();

            foreach (var word in words)
            {
                // Check if this word has synonyms
                var synonymGroup = EnhancedVariableSynonyms
                    .FirstOrDefault(kv => kv.Value.Contains(word, StringComparer.OrdinalIgnoreCase));

                if (!synonymGroup.Equals(default(KeyValuePair<string, List<string>>)))
                {
                    // Generate aliases with different synonyms
                    var newAliases = new List<string>();
                    
                    foreach (var existingAlias in aliases)
                    {
                        foreach (var synonym in synonymGroup.Value)
                        {
                            var aliased = Regex.Replace(
                                existingAlias,
                                $@"\b{word}\b",
                                synonym,
                                RegexOptions.IgnoreCase);
                            
                            if (aliased != existingAlias)
                            {
                                newAliases.Add(aliased);
                            }
                        }
                    }
                    
                    aliases.AddRange(newAliases.Take(5)); // Limit to prevent explosion
                }
            }

            return aliases.Distinct().Take(10).ToList(); // Max 10 variants
        }

        /// <summary>
        /// Extract numerical value with unit (e.g., "42.5 cm" -> (42.5, "cm"))
        /// </summary>
        public static (double? Value, string Unit) ExtractNumericalWithUnit(string text)
        {
            // Pattern: number followed by optional unit
            var pattern = @"(?<sign>[+-])?(?<num>\d+\.?\d*)\s*(?<unit>[a-zA-Z°%]+)?";
            var match = Regex.Match(text, pattern);

            if (match.Success)
            {
                var numStr = match.Groups["num"].Value;
                var sign = match.Groups["sign"].Value == "-" ? -1.0 : 1.0;
                var unit = match.Groups["unit"].Value;

                if (double.TryParse(numStr, out var value))
                {
                    return (value * sign, unit);
                }
            }

            // Fallback: Just extract number
            var numMatch = Regex.Match(text, @"[+-]?\d+\.?\d*");
            if (numMatch.Success && double.TryParse(numMatch.Value, out var fallbackValue))
            {
                return (fallbackValue, string.Empty);
            }

            return (null, string.Empty);
        }

        /// <summary>
        /// Check symbolic equivalence using MathNet.Symbolics
        /// Returns detailed result with confidence
        /// </summary>
        public static SymbolicEquivalenceResult CheckSymbolicEquivalence(
            string studentExpr,
            string modelExpr)
        {
            var result = new SymbolicEquivalenceResult();

            try
            {
                // Try direct parsing
                var studentParsed = Infix.ParseOrUndefined(studentExpr);
                var modelParsed = Infix.ParseOrUndefined(modelExpr);

                result.StudentParsed = studentParsed.ToString();
                result.ModelParsed = modelParsed.ToString();

                // Check if both parsed successfully
                if (studentParsed == Expression.Undefined || modelParsed == Expression.Undefined)
                {
                    result.IsEquivalent = false;
                    result.Confidence = 0.0;
                    result.Explanation = "Could not parse expressions";
                    return result;
                }

                // Simplify both expressions
                var studentSimplified = Algebraic.Expand(studentParsed);
                var modelSimplified = Algebraic.Expand(modelParsed);

                result.StudentSimplified = studentSimplified.ToString();
                result.ModelSimplified = modelSimplified.ToString();

                // Direct equality check
                if (studentSimplified.Equals(modelSimplified))
                {
                    result.IsEquivalent = true;
                    result.Confidence = 1.0;
                    result.Explanation = "Direct symbolic equality";
                    return result;
                }

                // Check if difference is zero
                var difference = Algebraic.Expand(studentSimplified - modelSimplified);
                result.Difference = difference.ToString();

                if (difference.Equals(Expression.Zero))
                {
                    result.IsEquivalent = true;
                    result.Confidence = 0.95;
                    result.Explanation = "Difference equals zero";
                    return result;
                }

                // Try numerical evaluation at sample points (fallback)
                var sampleEquivalent = TestNumericalEquivalence(studentSimplified, modelSimplified);
                if (sampleEquivalent)
                {
                    result.IsEquivalent = true;
                    result.Confidence = 0.85;
                    result.Explanation = "Numerically equivalent at test points";
                    return result;
                }

                result.IsEquivalent = false;
                result.Confidence = 0.9;
                result.Explanation = "Expressions are not equivalent";
            }
            catch (Exception ex)
            {
                result.IsEquivalent = false;
                result.Confidence = 0.0;
                result.Explanation = $"Evaluation error: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Test numerical equivalence at multiple sample points
        /// </summary>
        private static bool TestNumericalEquivalence(Expression expr1, Expression expr2)
        {
            try
            {
                // Get all symbols using algebraic manipulation
                var identifiers = new HashSet<string>();
                
                // Extract identifiers from both expressions by pattern matching
                var expr1Str = expr1.ToString();
                var expr2Str = expr2.ToString();
                
                // Simple variable extraction (letters)
                var varPattern = new System.Text.RegularExpressions.Regex(@"\b[a-zA-Z]\b");
                foreach (System.Text.RegularExpressions.Match match in varPattern.Matches(expr1Str))
                    identifiers.Add(match.Value);
                foreach (System.Text.RegularExpressions.Match match in varPattern.Matches(expr2Str))
                    identifiers.Add(match.Value);

                if (!identifiers.Any())
                    return false;

                // Test at multiple points
                var testPoints = new[] { 1.0, 2.0, 0.5, -1.0, 3.0 };
                var tolerance = 1e-6;

                foreach (var testValue in testPoints)
                {
                    var substitutions = identifiers.ToDictionary(
                        s => s,
                        s => (FloatingPoint)testValue);

                    try
                    {
                        var val1 = Evaluate.Evaluate(substitutions, expr1).RealValue;
                        var val2 = Evaluate.Evaluate(substitutions, expr2).RealValue;

                        if (double.IsNaN(val1) || double.IsInfinity(val1) || 
                            double.IsNaN(val2) || double.IsInfinity(val2))
                            continue;

                        var diff = Math.Abs(val1 - val2);
                        if (diff > tolerance)
                            return false;
                    }
                    catch
                    {
                        continue;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extract step-wise marks from a multi-step solution
        /// </summary>
        public static List<StepWiseMarks> EvaluateStepWise(
            string studentAnswer,
            string modelAnswer,
            double maxMarks)
        {
            var steps = new List<StepWiseMarks>();

            // Split by lines or semicolons
            var studentLines = SplitIntoSteps(studentAnswer);
            var modelLines = SplitIntoSteps(modelAnswer);

            var marksPerStep = maxMarks / Math.Max(modelLines.Count, 1);

            for (int i = 0; i < modelLines.Count && i < studentLines.Count; i++)
            {
                var studentStep = NormalizeExpression(studentLines[i]);
                var modelStep = NormalizeExpression(modelLines[i]);

                var equivalence = CheckSymbolicEquivalence(studentStep, modelStep);

                steps.Add(new StepWiseMarks
                {
                    StepNumber = i + 1,
                    StepDescription = $"Step {i + 1}: {modelStep}",
                    MaxMarks = marksPerStep,
                    MarksAwarded = equivalence.IsEquivalent ? marksPerStep : 0,
                    Status = equivalence.IsEquivalent ? "Complete" : "Incorrect",
                    Feedback = equivalence.Explanation
                });
            }

            return steps;
        }

        /// <summary>
        /// Split solution into logical steps
        /// </summary>
        private static List<string> SplitIntoSteps(string solution)
        {
            // Split by newlines, semicolons, or step markers
            var steps = Regex.Split(solution, @"\r?\n|;|step\s+\d+", RegexOptions.IgnoreCase)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToList();

            return steps;
        }
    }

    /// <summary>
    /// Result of symbolic equivalence check
    /// </summary>
    public class SymbolicEquivalenceResult
    {
        public bool IsEquivalent { get; set; }
        public double Confidence { get; set; }
        public string Explanation { get; set; } = string.Empty;
        public string StudentParsed { get; set; } = string.Empty;
        public string ModelParsed { get; set; } = string.Empty;
        public string StudentSimplified { get; set; } = string.Empty;
        public string ModelSimplified { get; set; } = string.Empty;
        public string Difference { get; set; } = string.Empty;
    }
}
