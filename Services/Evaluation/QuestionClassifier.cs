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
    /// Rule-based question classifier
    /// Identifies subject and question type without OpenAI
    /// </summary>
    public class QuestionClassifier : IQuestionClassifier
    {
        private readonly ILogger<QuestionClassifier> _logger;

        // Subject detection keywords
        private static readonly Dictionary<SubjectCategory, List<string>> SubjectKeywords = new()
        {
            [SubjectCategory.Mathematics] = new()
            {
                "calculate", "find the value", "solve", "equation", "formula", "prove",
                "derivative", "integral", "algebra", "geometry", "trigonometry",
                "area", "perimeter", "volume", "square root", "factor", "simplify",
                "polynomial", "quadratic", "linear", "theorem", "π", "sin", "cos", "tan"
            },
            [SubjectCategory.Physics] = new()
            {
                "force", "velocity", "acceleration", "newton", "energy", "power",
                "mass", "weight", "momentum", "friction", "gravity", "motion",
                "electric", "magnetic", "current", "voltage", "resistance", "circuit",
                "wave", "frequency", "wavelength", "light", "optics", "lens", "mirror",
                "heat", "temperature", "thermodynamics", "pressure", "density"
            },
            [SubjectCategory.Chemistry] = new()
            {
                "molecule", "atom", "element", "compound", "reaction", "chemical",
                "acid", "base", "pH", "salt", "oxidation", "reduction", "catalyst",
                "mole", "molarity", "solution", "periodic table", "valency", "bond",
                "ionic", "covalent", "organic", "inorganic", "carbon", "hydrogen"
            },
            [SubjectCategory.Biology] = new()
            {
                "cell", "organism", "plant", "animal", "photosynthesis", "respiration",
                "DNA", "gene", "chromosome", "evolution", "ecosystem", "species",
                "tissue", "organ", "system", "reproduction", "heredity", "mitosis",
                "protein", "enzyme", "digestive", "circulatory", "nervous"
            },
            [SubjectCategory.SocialScience] = new()
            {
                "history", "geography", "civics", "politics", "government", "democracy",
                "constitution", "economy", "culture", "society", "map", "latitude",
                "longitude", "climate", "population", "war", "independence", "freedom",
                "rights", "fundamental", "directive principles", "amendment"
            },
            [SubjectCategory.English] = new()
            {
                "essay", "paragraph", "story", "poem", "letter", "grammar", "comprehension",
                "write", "describe", "explain", "summarize", "author", "character",
                "plot", "theme", "metaphor", "simile", "noun", "verb", "adjective"
            },
            [SubjectCategory.Hindi] = new()
            {
                "निबंध", "कविता", "कहानी", "पत्र", "व्याकरण", "लेखक", "कवि",
                "विशेषण", "सर्वनाम", "क्रिया", "संज्ञा", "अनुच्छेद"
            }
        };

        // Question type detection patterns
        private static readonly Dictionary<QuestionType, List<string>> TypePatterns = new()
        {
            [QuestionType.Numerical] = new()
            {
                "find the value", "calculate", "compute", "what is the", "how much",
                "how many", "the answer is", "=", "solve for"
            },
            [QuestionType.Formula] = new()
            {
                "derive", "prove", "show that", "demonstrate", "formula", "expression",
                "relation", "establish"
            },
            [QuestionType.Definition] = new()
            {
                "define", "what is meant by", "meaning of", "state", "explain the term",
                "definition of"
            },
            [QuestionType.ShortAnswer] = new()
            {
                "briefly", "in short", "state", "mention", "list", "name", "2-3 lines",
                "short answer", "write short note"
            },
            [QuestionType.LongAnswer] = new()
            {
                "explain", "describe", "discuss", "elaborate", "analyze", "compare",
                "contrast", "evaluate", "in detail"
            },
            [QuestionType.Essay] = new()
            {
                "essay", "write an essay", "elaborate", "write in detail", "composition",
                "article"
            },
            [QuestionType.Derivation] = new()
            {
                "derive", "prove", "show that", "step by step", "derivation"
            },
            [QuestionType.Diagram] = new()
            {
                "draw", "diagram", "label", "sketch", "illustrate", "figure", "graph",
                "plot", "chart"
            }
        };

        public QuestionClassifier(ILogger<QuestionClassifier> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<QuestionClassification> ClassifyAsync(
            string questionText,
            string? syllabusContext = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(questionText))
            {
                return Task.FromResult(new QuestionClassification
                {
                    Subject = SubjectCategory.Unknown,
                    Type = QuestionType.Unknown,
                    SubjectConfidence = 0,
                    TypeConfidence = 0,
                    ReasoningTrace = "Empty question text"
                });
            }

            var lowerQuestion = questionText.ToLowerInvariant();
            var reasoning = new List<string>();

            // Classify subject
            var subjectScores = new Dictionary<SubjectCategory, double>();
            foreach (var (subject, keywords) in SubjectKeywords)
            {
                var matchCount = keywords.Count(k => lowerQuestion.Contains(k.ToLowerInvariant()));
                var score = (double)matchCount / keywords.Count;
                subjectScores[subject] = score;

                if (matchCount > 0)
                {
                    reasoning.Add($"Subject {subject}: {matchCount} keywords matched");
                }
            }

            // Check for mathematical symbols as strong indicator
            if (Regex.IsMatch(questionText, @"[\d+\-*/=()^√∫∑∏∂]") ||
                questionText.Contains("²") || questionText.Contains("³"))
            {
                subjectScores[SubjectCategory.Mathematics] =
                    Math.Max(subjectScores.GetValueOrDefault(SubjectCategory.Mathematics), 0.7);
                reasoning.Add("Mathematics: Detected mathematical symbols");
            }

            // Check for units (Physics/Chemistry indicator)
            if (Regex.IsMatch(questionText, @"\b(m/s|kg|N|J|W|V|A|Ω|mol|L|°C|K|Pa)\b"))
            {
                var current = Math.Max(
                    subjectScores.GetValueOrDefault(SubjectCategory.Physics),
                    subjectScores.GetValueOrDefault(SubjectCategory.Chemistry));
                subjectScores[SubjectCategory.Physics] = Math.Max(current, 0.6);
                reasoning.Add("Science: Detected SI units");
            }

            var bestSubject = subjectScores.OrderByDescending(s => s.Value).FirstOrDefault();
            var detectedSubject = bestSubject.Value > 0.1 ? bestSubject.Key : SubjectCategory.Unknown;

            // Classify question type
            var typeScores = new Dictionary<QuestionType, double>();
            foreach (var (type, patterns) in TypePatterns)
            {
                var matchCount = patterns.Count(p => lowerQuestion.Contains(p.ToLowerInvariant()));
                var score = (double)matchCount / patterns.Count;
                typeScores[type] = score;

                if (matchCount > 0)
                {
                    reasoning.Add($"Type {type}: {matchCount} patterns matched");
                }
            }

            // Check for marks to infer question type
            var marksMatch = Regex.Match(questionText, @"\[(\d+)\s*marks?\]", RegexOptions.IgnoreCase);
            if (marksMatch.Success && int.TryParse(marksMatch.Groups[1].Value, out var marks))
            {
                if (marks == 1 || marks == 2)
                {
                    typeScores[QuestionType.ShortAnswer] = Math.Max(typeScores.GetValueOrDefault(QuestionType.ShortAnswer), 0.5);
                    reasoning.Add($"Short answer inferred from {marks} marks");
                }
                else if (marks >= 3 && marks <= 5)
                {
                    typeScores[QuestionType.LongAnswer] = Math.Max(typeScores.GetValueOrDefault(QuestionType.LongAnswer), 0.5);
                    reasoning.Add($"Long answer inferred from {marks} marks");
                }
                else if (marks > 5)
                {
                    typeScores[QuestionType.Essay] = Math.Max(typeScores.GetValueOrDefault(QuestionType.Essay), 0.5);
                    reasoning.Add($"Essay inferred from {marks} marks");
                }
            }

            var bestType = typeScores.OrderByDescending(t => t.Value).FirstOrDefault();
            var detectedType = bestType.Value > 0.1 ? bestType.Key : QuestionType.ShortAnswer;

            var result = new QuestionClassification
            {
                Subject = detectedSubject,
                Type = detectedType,
                SubjectConfidence = bestSubject.Value,
                TypeConfidence = bestType.Value,
                ReasoningTrace = string.Join("; ", reasoning)
            };

            _logger.LogInformation(
                "Question classified as {Subject} ({SubjectConf:F2}) / {Type} ({TypeConf:F2})",
                result.Subject, result.SubjectConfidence, result.Type, result.TypeConfidence);

            return Task.FromResult(result);
        }
    }
}
