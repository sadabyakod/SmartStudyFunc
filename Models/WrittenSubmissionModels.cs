using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SmartStudyFunc.Models
{
    /// <summary>
    /// Status of written answer submission processing.
    /// Flow: Uploaded → OcrProcessing → Evaluating → Completed (or Failed on error)
    /// </summary>
    public enum WrittenSubmissionStatus
    {
        Uploaded = 0,
        OcrProcessing = 1,
        Evaluating = 2,
        Completed = 3,
        Failed = 4
    }

    /// <summary>
    /// Queue message schema for written-submission-processing queue.
    /// Matches exact schema from API App Service.
    /// </summary>
    public class WrittenSubmissionProcessingMessage
    {
        [JsonPropertyName("writtenSubmissionId")]
        public Guid WrittenSubmissionId { get; set; }
        
        [JsonPropertyName("examId")]
        public string ExamId { get; set; } = string.Empty;
        
        [JsonPropertyName("studentId")]
        public string StudentId { get; set; } = string.Empty;
        
        [JsonPropertyName("filePaths")]
        public List<string> FilePaths { get; set; } = new();
        
        [JsonPropertyName("submittedAt")]
        public DateTime SubmittedAt { get; set; }
        
        [JsonPropertyName("priority")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int Priority { get; set; } = 1;
        
        [JsonPropertyName("retryCount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int RetryCount { get; set; } = 0;
    }

    /// <summary>
    /// Queue message for AI evaluation
    /// </summary>
    public class WrittenAnswerEvaluationMessage
    {
        public Guid WrittenSubmissionId { get; set; }
    }

    /// <summary>
    /// Written submission entity for database persistence.
    /// Status flow: Uploaded → OcrProcessing → Evaluating → Completed | Failed
    /// </summary>
    public class WrittenSubmission
    {
        public Guid Id { get; set; }
        public string ExamId { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
        public WrittenSubmissionStatus Status { get; set; } = WrittenSubmissionStatus.Uploaded;
        public string? ExtractedText { get; set; }
        public string? ExtractedTextJson { get; set; } // JSON per page
        public string? ExtractedTextBlobPath { get; set; }
        public decimal? TotalScore { get; set; }
        public decimal? MaxPossibleScore { get; set; }
        public decimal? Percentage { get; set; }
        public string? Grade { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? OcrStartedAt { get; set; }
        public DateTime? OcrCompletedAt { get; set; }
        public DateTime? EvaluationStartedAt { get; set; }
        public DateTime? EvaluatedAt { get; set; }
        public int RetryCount { get; set; }
        public long? OcrProcessingTimeMs { get; set; }
        public long? EvaluationProcessingTimeMs { get; set; }
        
        // MCQ-specific columns (populated by backend API)
        public string? McqAnswers { get; set; } // JSON array of MCQ answers from backend
        public decimal? McqScore { get; set; }
        public decimal? McqTotalMarks { get; set; }
    }

    /// <summary>
    /// Individual question evaluation result
    /// </summary>
    public class WrittenQuestionEvaluation
    {
        public Guid Id { get; set; }
        public Guid WrittenSubmissionId { get; set; }
        public string QuestionId { get; set; } = string.Empty;
        public int QuestionNumber { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string ExtractedAnswer { get; set; } = string.Empty;
        public string ModelAnswer { get; set; } = string.Empty;
        public decimal MaxScore { get; set; }
        public decimal AwardedScore { get; set; }
        public string Feedback { get; set; } = string.Empty;
        public string RubricBreakdown { get; set; } = string.Empty;
        public DateTime EvaluatedAt { get; set; }
        public bool IsMcq { get; set; }
    }

    /// <summary>
    /// Overall evaluation result for a written submission
    /// </summary>
    public class WrittenEvaluationResult
    {
        public Guid WrittenSubmissionId { get; set; }
        public string ExamId { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public decimal TotalScore { get; set; }
        public decimal MaxPossibleScore { get; set; }
        public decimal Percentage { get; set; }
        public string Grade { get; set; } = string.Empty;
        public List<WrittenQuestionEvaluation> QuestionEvaluations { get; set; } = new();
        public DateTime EvaluatedAt { get; set; }
        
        // Separate scoring for MCQ and Subjective questions
        public decimal McqScore { get; set; }
        public decimal McqMaxScore { get; set; }
        public decimal SubjectiveScore { get; set; }
        public decimal SubjectiveMaxScore { get; set; }
        public int McqCount { get; set; }
        public int SubjectiveCount { get; set; }
    }

    /// <summary>
    /// Exam question with rubric for evaluation
    /// </summary>
    public class ExamQuestionWithRubric
    {
        public string QuestionId { get; set; } = string.Empty;
        public int QuestionNumber { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string ModelAnswer { get; set; } = string.Empty;
        public decimal MaxScore { get; set; }
        public string Rubric { get; set; } = string.Empty;
        public bool IsMcq { get; set; }
        public List<string> McqOptions { get; set; } = new();
        public List<string> Keywords { get; set; } = new();
        
        // Syllabus metadata for RAG lookup
        public string ClassName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Chapter { get; set; } = string.Empty;
    }

    /// <summary>
    /// OCR result for a single page/file
    /// </summary>
    public class OcrPageResult
    {
        public int PageNumber { get; set; }
        public string BlobPath { get; set; } = string.Empty;
        public string ExtractedText { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // STEP-WISE BOARD BLUEPRINT MARKING MODELS
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A single step in the marking scheme (board blueprint style)
    /// </summary>
    public class MarkingStep
    {
        [JsonPropertyName("stepNumber")]
        public int StepNumber { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();

        [JsonPropertyName("marks")]
        public decimal Marks { get; set; }
    }

    /// <summary>
    /// Expected answer generated from syllabus with step-wise marking scheme
    /// </summary>
    public class ExpectedAnswer
    {
        [JsonPropertyName("summary")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("steps")]
        public List<MarkingStep> Steps { get; set; } = new();

        [JsonPropertyName("syllabusChunkIds")]
        public List<int> SyllabusChunkIds { get; set; } = new();
    }

    /// <summary>
    /// Evaluation result for a single step
    /// </summary>
    public class StepEvaluation
    {
        [JsonPropertyName("stepNumber")]
        public int StepNumber { get; set; }

        [JsonPropertyName("awardedMarks")]
        public decimal AwardedMarks { get; set; }

        [JsonPropertyName("maxMarks")]
        public decimal MaxMarks { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Complete student evaluation with step-wise breakdown
    /// </summary>
    public class StudentStepWiseEvaluation
    {
        [JsonPropertyName("steps")]
        public List<StepEvaluation> Steps { get; set; } = new();

        [JsonPropertyName("totalAwardedMarks")]
        public decimal TotalAwardedMarks { get; set; }

        [JsonPropertyName("confidenceScore")]
        public decimal ConfidenceScore { get; set; }
    }

    /// <summary>
    /// Complete step-wise evaluation result for a single question (board blueprint style)
    /// </summary>
    public class StepWiseQuestionEvaluation
    {
        [JsonPropertyName("questionNumber")]
        public int QuestionNumber { get; set; }

        [JsonPropertyName("maxMarks")]
        public decimal MaxMarks { get; set; }

        [JsonPropertyName("expectedAnswer")]
        public ExpectedAnswer ExpectedAnswer { get; set; } = new();

        [JsonPropertyName("studentEvaluation")]
        public StudentStepWiseEvaluation StudentEvaluation { get; set; } = new();

        [JsonPropertyName("overallFeedback")]
        public string OverallFeedback { get; set; } = string.Empty;
    }

    /// <summary>
    /// Syllabus chunk with similarity score for RAG retrieval
    /// </summary>
    public class SyllabusChunk
    {
        public int ChunkId { get; set; }
        public string ChunkText { get; set; } = string.Empty;
        public string TopicTitle { get; set; } = string.Empty;
        public double Similarity { get; set; }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // UI RESPONSE MODELS - Formatted for mobile app consumption
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// MCQ result formatted for UI display
    /// </summary>
    public class McqResultDto
    {
        [JsonPropertyName("questionId")]
        public string QuestionId { get; set; } = string.Empty;

        [JsonPropertyName("questionNumber")]
        public int QuestionNumber { get; set; }

        [JsonPropertyName("questionText")]
        public string QuestionText { get; set; } = string.Empty;

        [JsonPropertyName("selectedOption")]
        public string SelectedOption { get; set; } = string.Empty;

        [JsonPropertyName("correctAnswer")]
        public string CorrectAnswer { get; set; } = string.Empty;

        [JsonPropertyName("isCorrect")]
        public bool IsCorrect { get; set; }

        [JsonPropertyName("marksAwarded")]
        public decimal MarksAwarded { get; set; }

        [JsonPropertyName("maxMarks")]
        public decimal MaxMarks { get; set; }

        [JsonPropertyName("options")]
        public List<string> Options { get; set; } = new();
    }

    /// <summary>
    /// Step analysis for subjective answer evaluation
    /// </summary>
    public class StepAnalysisDto
    {
        [JsonPropertyName("step")]
        public int Step { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("isCorrect")]
        public bool IsCorrect { get; set; }

        [JsonPropertyName("marksAwarded")]
        public decimal MarksAwarded { get; set; }

        [JsonPropertyName("maxMarksForStep")]
        public decimal MaxMarksForStep { get; set; }

        [JsonPropertyName("feedback")]
        public string Feedback { get; set; } = string.Empty;
    }

    /// <summary>
    /// Subjective result formatted for UI display
    /// </summary>
    public class SubjectiveResultDto
    {
        [JsonPropertyName("questionId")]
        public string QuestionId { get; set; } = string.Empty;

        [JsonPropertyName("questionNumber")]
        public int QuestionNumber { get; set; }

        [JsonPropertyName("questionText")]
        public string QuestionText { get; set; } = string.Empty;

        [JsonPropertyName("earnedMarks")]
        public decimal EarnedMarks { get; set; }

        [JsonPropertyName("maxMarks")]
        public decimal MaxMarks { get; set; }

        [JsonPropertyName("isFullyCorrect")]
        public bool IsFullyCorrect { get; set; }

        [JsonPropertyName("expectedAnswer")]
        public string ExpectedAnswer { get; set; } = string.Empty;

        [JsonPropertyName("studentAnswerEcho")]
        public string StudentAnswerEcho { get; set; } = string.Empty;

        [JsonPropertyName("overallFeedback")]
        public string OverallFeedback { get; set; } = string.Empty;

        [JsonPropertyName("stepAnalysis")]
        public List<StepAnalysisDto> StepAnalysis { get; set; } = new();
    }

    /// <summary>
    /// Complete evaluation response formatted for UI consumption
    /// </summary>
    public class EvaluationResultDto
    {
        [JsonPropertyName("examId")]
        public string ExamId { get; set; } = string.Empty;

        [JsonPropertyName("studentId")]
        public string StudentId { get; set; } = string.Empty;

        [JsonPropertyName("examTitle")]
        public string ExamTitle { get; set; } = string.Empty;

        [JsonPropertyName("mcqScore")]
        public decimal McqScore { get; set; }

        [JsonPropertyName("mcqTotalMarks")]
        public decimal McqTotalMarks { get; set; }

        [JsonPropertyName("mcqResults")]
        public List<McqResultDto> McqResults { get; set; } = new();

        [JsonPropertyName("subjectiveScore")]
        public decimal SubjectiveScore { get; set; }

        [JsonPropertyName("subjectiveTotalMarks")]
        public decimal SubjectiveTotalMarks { get; set; }

        [JsonPropertyName("subjectiveResults")]
        public List<SubjectiveResultDto> SubjectiveResults { get; set; } = new();

        [JsonPropertyName("grandScore")]
        public decimal GrandScore { get; set; }

        [JsonPropertyName("grandTotalMarks")]
        public decimal GrandTotalMarks { get; set; }

        [JsonPropertyName("percentage")]
        public decimal Percentage { get; set; }

        [JsonPropertyName("grade")]
        public string Grade { get; set; } = string.Empty;

        [JsonPropertyName("passed")]
        public bool Passed { get; set; }

        [JsonPropertyName("evaluatedAt")]
        public DateTime EvaluatedAt { get; set; }
    }
}
