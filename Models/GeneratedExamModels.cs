using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SmartStudyFunc.Models
{
    /// <summary>
    /// Generated exam paper entity - stored in Azure SQL
    /// </summary>
    public class GeneratedExamPaper
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Human-readable paper ID like "PAPER-20251222-001"
        /// </summary>
        public string PaperId { get; set; } = string.Empty;
        
        /// <summary>
        /// Links to GeneratedExams.ExamId for backwards compatibility
        /// </summary>
        public string ExamId { get; set; } = string.Empty;
        
        /// <summary>
        /// Random seed for reproducibility
        /// </summary>
        public int? Seed { get; set; }
        
        /// <summary>
        /// Paper version number
        /// </summary>
        public int Version { get; set; } = 1;
        
        public string? Subject { get; set; }
        public string? Grade { get; set; }
        public string? Chapter { get; set; }
        public int TotalMarks { get; set; }
        public int TotalQuestions { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
        public bool IsActive { get; set; } = true;
        
        /// <summary>
        /// Questions in this paper
        /// </summary>
        [JsonIgnore]
        public List<GeneratedExamQuestion> Questions { get; set; } = new();
    }

    /// <summary>
    /// Generated exam question entity - stored in Azure SQL with rubric blob reference
    /// </summary>
    public class GeneratedExamQuestion
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// Foreign key to GeneratedExamPapers.PaperId
        /// </summary>
        public string PaperId { get; set; } = string.Empty;
        
        /// <summary>
        /// Unique question ID like "Q1", "Q2", or section-based like "A1", "B2"
        /// </summary>
        public string QuestionId { get; set; } = string.Empty;
        
        public int QuestionNumber { get; set; }
        
        /// <summary>
        /// Section: A, B, C, D
        /// </summary>
        public string? Section { get; set; }
        
        public string QuestionText { get; set; } = string.Empty;
        
        /// <summary>
        /// Question type: 'mcq', 'subjective', 'short-answer', 'essay'
        /// </summary>
        public string QuestionType { get; set; } = "subjective";
        
        public int TotalMarks { get; set; } = 1;
        
        /// <summary>
        /// Short model answer for display
        /// </summary>
        public string? ModelAnswer { get; set; }
        
        /// <summary>
        /// Blob path: modalquestions-rubrics/paper-{PaperId}/question-{QuestionId}.json
        /// </summary>
        public string? RubricBlobPath { get; set; }
        
        /// <summary>
        /// Keywords for evaluation (JSON array)
        /// </summary>
        public List<string> Keywords { get; set; } = new();
        
        public string? Topic { get; set; }
        
        /// <summary>
        /// MCQ options (JSON array)
        /// </summary>
        public List<string> McqOptions { get; set; } = new();
        
        /// <summary>
        /// Correct option for MCQ: A, B, C, D
        /// </summary>
        public string? CorrectOption { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        
        /// <summary>
        /// Check if this is an MCQ question
        /// </summary>
        [JsonIgnore]
        public bool IsMcq => QuestionType.Equals("mcq", StringComparison.OrdinalIgnoreCase) || 
                             McqOptions.Count > 0;
    }

    /// <summary>
    /// Detailed rubric stored in blob storage
    /// Path: modalquestions-rubrics/paper-{PaperId}/question-{QuestionId}.json
    /// </summary>
    public class QuestionRubric
    {
        [JsonPropertyName("questionId")]
        public string QuestionId { get; set; } = string.Empty;
        
        [JsonPropertyName("paperId")]
        public string PaperId { get; set; } = string.Empty;
        
        [JsonPropertyName("questionNumber")]
        public int QuestionNumber { get; set; }
        
        [JsonPropertyName("questionText")]
        public string QuestionText { get; set; } = string.Empty;
        
        [JsonPropertyName("questionType")]
        public string QuestionType { get; set; } = "subjective";
        
        [JsonPropertyName("totalMarks")]
        public int TotalMarks { get; set; }
        
        [JsonPropertyName("modelAnswer")]
        public string ModelAnswer { get; set; } = string.Empty;
        
        [JsonPropertyName("topic")]
        public string? Topic { get; set; }
        
        [JsonPropertyName("subject")]
        public string? Subject { get; set; }
        
        [JsonPropertyName("grade")]
        public string? Grade { get; set; }
        
        [JsonPropertyName("chapter")]
        public string? Chapter { get; set; }
        
        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();
        
        /// <summary>
        /// Step-wise marking scheme (board blueprint style)
        /// </summary>
        [JsonPropertyName("markingSteps")]
        public List<RubricMarkingStep> MarkingSteps { get; set; } = new();
        
        /// <summary>
        /// General rubric text for AI evaluation
        /// </summary>
        [JsonPropertyName("rubricText")]
        public string RubricText { get; set; } = string.Empty;
        
        /// <summary>
        /// MCQ options if applicable
        /// </summary>
        [JsonPropertyName("mcqOptions")]
        public List<string> McqOptions { get; set; } = new();
        
        [JsonPropertyName("correctOption")]
        public string? CorrectOption { get; set; }
        
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Step in marking scheme for step-wise evaluation
    /// </summary>
    public class RubricMarkingStep
    {
        [JsonPropertyName("stepNumber")]
        public int StepNumber { get; set; }
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonPropertyName("maxMarks")]
        public decimal MaxMarks { get; set; }
        
        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();
        
        [JsonPropertyName("criteria")]
        public string? Criteria { get; set; }
    }

    /// <summary>
    /// Request model for generating exam paper
    /// </summary>
    public class GenerateExamRequest
    {
        [JsonPropertyName("examId")]
        public string? ExamId { get; set; }
        
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;
        
        [JsonPropertyName("grade")]
        public string Grade { get; set; } = string.Empty;
        
        [JsonPropertyName("chapter")]
        public string? Chapter { get; set; }
        
        [JsonPropertyName("seed")]
        public int? Seed { get; set; }
        
        /// <summary>
        /// Part configurations: { "A": { "count": 10, "marks": 1, "type": "mcq" }, ... }
        /// </summary>
        [JsonPropertyName("parts")]
        public Dictionary<string, PartConfig>? Parts { get; set; }
    }

    /// <summary>
    /// Configuration for an exam part
    /// </summary>
    public class PartConfig
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }
        
        [JsonPropertyName("marks")]
        public int Marks { get; set; }
        
        [JsonPropertyName("type")]
        public string Type { get; set; } = "subjective";
    }
}
