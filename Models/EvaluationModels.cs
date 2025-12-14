using System.Collections.Generic;

namespace SmartStudyFunc.Models
{
    /// <summary>
    /// Result of AI scoring evaluation
    /// </summary>
    public class ScoringResult
    {
        public double Score { get; set; }
        public int MaxMarks { get; set; }
        public string Feedback { get; set; } = string.Empty;
        public List<string> MissingPoints { get; set; } = new();
        public List<string> Strengths { get; set; } = new();
        public string ImprovementSuggestion { get; set; } = string.Empty;
        public List<string> KeywordsMatched { get; set; } = new();
        public List<string> MissingKeywords { get; set; } = new();
        public bool UsedFallback { get; set; } = false;
    }

    /// <summary>
    /// Request for answer evaluation
    /// </summary>
    public class EvaluateAnswerRequest
    {
        public string ExamId { get; set; } = string.Empty;
        public Guid QuestionId { get; set; }
        public string StudentAnswerText { get; set; } = string.Empty;
        public string? ExtractedText { get; set; }
        public string? BlobPath { get; set; }
        public Guid? WrittenSubmissionId { get; set; }
    }

    /// <summary>
    /// Response from answer evaluation
    /// </summary>
    public class EvaluateAnswerResponse
    {
        public bool Success { get; set; }
        public int EvaluationId { get; set; }
        public string ExamId { get; set; } = string.Empty;
        public Guid QuestionId { get; set; }
        public double Score { get; set; }
        public int MaxMarks { get; set; }
        public double Percentage { get; set; }
        public string Feedback { get; set; } = string.Empty;
        public string Strengths { get; set; } = string.Empty;
        public string Improvements { get; set; } = string.Empty;
        public List<string> KeywordsMatched { get; set; } = new();
        public List<string> MissingKeywords { get; set; } = new();
        public bool UsedFallback { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Response from OCR upload
    /// </summary>
    public class UploadAnswerResponse
    {
        public bool Success { get; set; }
        public int ExamId { get; set; }
        public int QuestionId { get; set; }
        public string ExtractedText { get; set; } = string.Empty;
        public int ExtractedLength { get; set; }
        public string BlobPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Batch evaluation request
    /// </summary>
    public class BatchEvaluateRequest
    {
        public List<EvaluateAnswerRequest> Evaluations { get; set; } = new();
    }

    /// <summary>
    /// Batch evaluation response
    /// </summary>
    public class BatchEvaluateResponse
    {
        public bool Success { get; set; }
        public int TotalRequested { get; set; }
        public int TotalProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<EvaluateAnswerResponse> Results { get; set; } = new();
    }
}
