using System;
using System.Collections.Generic;

namespace SmartStudyFunc.Models
{
    /// <summary>
    /// Subject categories for evaluation routing
    /// </summary>
    public enum SubjectCategory
    {
        Mathematics,
        Physics,
        Chemistry,
        Biology,
        SocialScience,
        English,
        Hindi,
        RegionalLanguage,
        Unknown
    }

    /// <summary>
    /// Question types that determine evaluation strategy
    /// </summary>
    public enum QuestionType
    {
        Numerical,              // Direct number answer (e.g., "Find the area: __")
        Formula,                // Requires formula (e.g., "Derive F=ma")
        Definition,             // Fact-based definition
        ShortAnswer,            // 2-3 sentences
        LongAnswer,             // Paragraph
        Essay,                  // Multi-paragraph
        Derivation,             // Step-by-step mathematical/scientific proof
        Diagram,                // Labeling/drawing
        MultipleChoice,         // MCQ (handled separately, but included for completeness)
        Unknown
    }

    /// <summary>
    /// Classification result for a question
    /// </summary>
    public class QuestionClassification
    {
        public SubjectCategory Subject { get; set; }
        public QuestionType Type { get; set; }
        public double SubjectConfidence { get; set; }
        public double TypeConfidence { get; set; }
        public string ReasoningTrace { get; set; } = string.Empty;
    }

    /// <summary>
    /// Standard output from all evaluation engines
    /// MANDATORY: All engines must return this format
    /// </summary>
    public class EvaluationEngineResult
    {
        /// <summary>
        /// Final marks awarded (rule-based decision, NOT from OpenAI)
        /// </summary>
        public double MarksAwarded { get; set; }

        /// <summary>
        /// Maximum possible marks for this question
        /// </summary>
        public double MaxMarks { get; set; }

        /// <summary>
        /// Confidence in the evaluation (0.0 - 1.0)
        /// Low confidence triggers teacher review
        /// </summary>
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// Human-readable explanation of how marks were calculated
        /// Must be auditable and explainable to teachers
        /// </summary>
        public string EvaluationReason { get; set; } = string.Empty;

        /// <summary>
        /// Flag indicating manual teacher review is needed
        /// </summary>
        public bool NeedsReview { get; set; }

        /// <summary>
        /// Feedback for the student (can use OpenAI)
        /// </summary>
        public string StudentFeedback { get; set; } = string.Empty;

        /// <summary>
        /// Strengths identified in the answer
        /// </summary>
        public List<string> Strengths { get; set; } = new();

        /// <summary>
        /// Improvement suggestions
        /// </summary>
        public List<string> Improvements { get; set; } = new();

        /// <summary>
        /// Keywords/concepts that were matched (for traceability)
        /// </summary>
        public List<string> MatchedKeywords { get; set; } = new();

        /// <summary>
        /// Keywords/concepts that were missing
        /// </summary>
        public List<string> MissingKeywords { get; set; } = new();

        /// <summary>
        /// Step-wise marks breakdown (for Math/Science)
        /// </summary>
        public List<StepWiseMarks> StepWiseBreakdown { get; set; } = new();

        /// <summary>
        /// Which engine processed this evaluation (for auditing)
        /// </summary>
        public string ProcessedBy { get; set; } = string.Empty;

        /// <summary>
        /// Detailed trace of rules applied (for debugging/auditing)
        /// </summary>
        public Dictionary<string, object> AuditTrail { get; set; } = new();
    }

    /// <summary>
    /// Context provided to evaluation engines
    /// </summary>
    public class EvaluationContext
    {
        public string QuestionId { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public string StudentAnswer { get; set; } = string.Empty;
        public string ModelAnswer { get; set; } = string.Empty;
        public double MaxMarks { get; set; }
        public List<string> Keywords { get; set; } = new();
        public SubjectCategory Subject { get; set; }
        public QuestionType Type { get; set; }
        public int ClassLevel { get; set; } // 6-12
        public string SyllabusReference { get; set; } = string.Empty; // Blob path
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Configuration for subject-specific evaluation rules
    /// Loaded from Azure Blob (JSON format)
    /// </summary>
    public class SubjectEvaluationConfig
    {
        public string Subject { get; set; } = string.Empty;
        public Dictionary<string, FormulaRule> Formulas { get; set; } = new();
        public Dictionary<string, UnitEquivalence> Units { get; set; } = new();
        public List<string> RequiredKeywords { get; set; } = new();
        public List<string> BonusKeywords { get; set; } = new();
        public double KeywordMatchThreshold { get; set; } = 0.5;
        public bool AllowPartialCredit { get; set; } = true;
        public Dictionary<string, double> RubricWeights { get; set; } = new();
    }

    /// <summary>
    /// Formula equivalence rule
    /// </summary>
    public class FormulaRule
    {
        public string CanonicalForm { get; set; } = string.Empty;
        public List<string> AlternativeForms { get; set; } = new();
        public List<string> Variables { get; set; } = new();
        public Dictionary<string, List<string>> VariableSynonyms { get; set; } = new();
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Unit equivalence for Physics/Chemistry
    /// </summary>
    public class UnitEquivalence
    {
        public string BaseUnit { get; set; } = string.Empty;
        public Dictionary<string, double> Conversions { get; set; } = new();
        public List<string> AcceptableNotations { get; set; } = new();
    }

    /// <summary>
    /// Rubric for language evaluation
    /// </summary>
    public class LanguageRubric
    {
        public double GrammarWeight { get; set; } = 0.25;
        public double StructureWeight { get; set; } = 0.25;
        public double RelevanceWeight { get; set; } = 0.30;
        public double VocabularyWeight { get; set; } = 0.20;
        public Dictionary<string, string> GrammarRules { get; set; } = new();
        public List<string> RequiredElements { get; set; } = new();
    }

    /// <summary>
    /// Mathematical equivalence check result
    /// </summary>
    public class MathEquivalenceResult
    {
        public bool IsEquivalent { get; set; }
        public double Confidence { get; set; }
        public string NormalizedStudent { get; set; } = string.Empty;
        public string NormalizedModel { get; set; } = string.Empty;
        public List<string> TransformationsApplied { get; set; } = new();
        public string Explanation { get; set; } = string.Empty;
    }
}
