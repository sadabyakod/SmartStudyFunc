using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// PRODUCTION-GRADE: Comprehensive audit logging for evaluation system
    /// Provides full traceability for teacher review and compliance
    /// </summary>
    public class EvaluationAuditLogger
    {
        private readonly ILogger<EvaluationAuditLogger> _logger;
        private readonly string _connectionString;

        public EvaluationAuditLogger(
            ILogger<EvaluationAuditLogger> logger,
            string connectionString)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <summary>
        /// Log complete evaluation audit trail to database
        /// Includes: engine used, rules applied, confidence scores
        /// </summary>
        public async Task LogEvaluationAsync(
            EvaluationAuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                // Insert into EvaluationAuditLog table
                var sql = @"
                    INSERT INTO EvaluationAuditLog (
                        EvaluationId,
                        QuestionId,
                        ExamId,
                        UserId,
                        EngineName,
                        SubjectCategory,
                        QuestionType,
                        ClassLevel,
                        StudentAnswer,
                        ModelAnswer,
                        MarksAwarded,
                        MaxMarks,
                        ConfidenceScore,
                        NeedsReview,
                        EvaluationReason,
                        MatchedKeywords,
                        MissingKeywords,
                        StepWiseBreakdown,
                        AuditTrail,
                        EvaluatedAt,
                        ProcessingTimeMs
                    ) VALUES (
                        @EvaluationId,
                        @QuestionId,
                        @ExamId,
                        @UserId,
                        @EngineName,
                        @SubjectCategory,
                        @QuestionType,
                        @ClassLevel,
                        @StudentAnswer,
                        @ModelAnswer,
                        @MarksAwarded,
                        @MaxMarks,
                        @ConfidenceScore,
                        @NeedsReview,
                        @EvaluationReason,
                        @MatchedKeywords,
                        @MissingKeywords,
                        @StepWiseBreakdown,
                        @AuditTrail,
                        @EvaluatedAt,
                        @ProcessingTimeMs
                    )";

                var parameters = new
                {
                    EvaluationId = entry.EvaluationId,
                    QuestionId = entry.QuestionId,
                    ExamId = entry.ExamId,
                    UserId = entry.UserId,
                    EngineName = entry.EngineName,
                    SubjectCategory = entry.SubjectCategory.ToString(),
                    QuestionType = entry.QuestionType.ToString(),
                    ClassLevel = entry.ClassLevel,
                    StudentAnswer = entry.StudentAnswer,
                    ModelAnswer = entry.ModelAnswer,
                    MarksAwarded = entry.MarksAwarded,
                    MaxMarks = entry.MaxMarks,
                    ConfidenceScore = entry.ConfidenceScore,
                    NeedsReview = entry.NeedsReview,
                    EvaluationReason = entry.EvaluationReason,
                    MatchedKeywords = JsonSerializer.Serialize(entry.MatchedKeywords),
                    MissingKeywords = JsonSerializer.Serialize(entry.MissingKeywords),
                    StepWiseBreakdown = JsonSerializer.Serialize(entry.StepWiseBreakdown),
                    AuditTrail = JsonSerializer.Serialize(entry.AuditTrail),
                    EvaluatedAt = entry.EvaluatedAt,
                    ProcessingTimeMs = entry.ProcessingTimeMs
                };

                await connection.ExecuteAsync(sql, parameters);

                _logger.LogInformation(
                    "Logged evaluation audit: {EvaluationId} - {Engine} - {Confidence:F2}",
                    entry.EvaluationId, entry.EngineName, entry.ConfidenceScore);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log evaluation audit: {EvaluationId}", entry.EvaluationId);
                // Don't throw - audit failure should not block evaluation
            }
        }

        /// <summary>
        /// Retrieve audit trail for teacher review
        /// </summary>
        public async Task<EvaluationAuditEntry?> GetAuditTrailAsync(
            string evaluationId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                var sql = @"
                    SELECT 
                        EvaluationId,
                        QuestionId,
                        ExamId,
                        UserId,
                        EngineName,
                        SubjectCategory,
                        QuestionType,
                        ClassLevel,
                        StudentAnswer,
                        ModelAnswer,
                        MarksAwarded,
                        MaxMarks,
                        ConfidenceScore,
                        NeedsReview,
                        EvaluationReason,
                        MatchedKeywords,
                        MissingKeywords,
                        StepWiseBreakdown,
                        AuditTrail,
                        EvaluatedAt,
                        ProcessingTimeMs
                    FROM EvaluationAuditLog
                    WHERE EvaluationId = @EvaluationId";

                var result = await connection.QueryFirstOrDefaultAsync<dynamic>(
                    sql,
                    new { EvaluationId = evaluationId });

                if (result == null)
                    return null;

                return MapToAuditEntry(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve audit trail: {EvaluationId}", evaluationId);
                return null;
            }
        }

        /// <summary>
        /// Get evaluations that need teacher review
        /// </summary>
        public async Task<List<EvaluationAuditEntry>> GetEvaluationsNeedingReviewAsync(
            int examId,
            double confidenceThreshold = 0.7,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                var sql = @"
                    SELECT 
                        EvaluationId,
                        QuestionId,
                        ExamId,
                        UserId,
                        EngineName,
                        SubjectCategory,
                        QuestionType,
                        ConfidenceScore,
                        MarksAwarded,
                        MaxMarks,
                        EvaluationReason,
                        NeedsReview,
                        EvaluatedAt
                    FROM EvaluationAuditLog
                    WHERE ExamId = @ExamId
                      AND (NeedsReview = 1 OR ConfidenceScore < @Threshold)
                    ORDER BY ConfidenceScore ASC, EvaluatedAt DESC";

                var results = await connection.QueryAsync<dynamic>(
                    sql,
                    new { ExamId = examId, Threshold = confidenceThreshold });

                return results.Select(MapToAuditEntry).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get evaluations needing review: ExamId {ExamId}", examId);
                return new List<EvaluationAuditEntry>();
            }
        }

        /// <summary>
        /// Get evaluation statistics by engine
        /// </summary>
        public async Task<Dictionary<string, EngineStatistics>> GetEngineStatisticsAsync(
            int? examId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                var sql = @"
                    SELECT 
                        EngineName,
                        COUNT(*) as TotalEvaluations,
                        AVG(ConfidenceScore) as AvgConfidence,
                        AVG(ProcessingTimeMs) as AvgProcessingTime,
                        SUM(CASE WHEN NeedsReview = 1 THEN 1 ELSE 0 END) as ReviewCount,
                        AVG(MarksAwarded / NULLIF(MaxMarks, 0)) as AvgScore
                    FROM EvaluationAuditLog
                    WHERE (@ExamId IS NULL OR ExamId = @ExamId)
                    GROUP BY EngineName";

                var results = await connection.QueryAsync<dynamic>(
                    sql,
                    new { ExamId = examId });

                var stats = new Dictionary<string, EngineStatistics>();

                foreach (var row in results)
                {
                    stats[row.EngineName] = new EngineStatistics
                    {
                        EngineName = row.EngineName,
                        TotalEvaluations = row.TotalEvaluations,
                        AverageConfidence = row.AvgConfidence,
                        AverageProcessingTimeMs = row.AvgProcessingTime,
                        ReviewCount = row.ReviewCount,
                        AverageScore = row.AvgScore
                    };
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get engine statistics");
                return new Dictionary<string, EngineStatistics>();
            }
        }

        private EvaluationAuditEntry MapToAuditEntry(dynamic row)
        {
            return new EvaluationAuditEntry
            {
                EvaluationId = row.EvaluationId,
                QuestionId = row.QuestionId,
                ExamId = row.ExamId,
                UserId = row.UserId,
                EngineName = row.EngineName,
                SubjectCategory = Enum.Parse<SubjectCategory>(row.SubjectCategory),
                QuestionType = Enum.Parse<QuestionType>(row.QuestionType),
                ClassLevel = row.ClassLevel,
                MarksAwarded = row.MarksAwarded,
                MaxMarks = row.MaxMarks,
                ConfidenceScore = row.ConfidenceScore,
                NeedsReview = row.NeedsReview,
                EvaluationReason = row.EvaluationReason,
                EvaluatedAt = row.EvaluatedAt
            };
        }
    }

    /// <summary>
    /// Audit log entry for database persistence
    /// </summary>
    public class EvaluationAuditEntry
    {
        public string EvaluationId { get; set; } = Guid.NewGuid().ToString();
        public string QuestionId { get; set; } = string.Empty;
        public int ExamId { get; set; }
        public int UserId { get; set; }
        public string EngineName { get; set; } = string.Empty;
        public SubjectCategory SubjectCategory { get; set; }
        public QuestionType QuestionType { get; set; }
        public int ClassLevel { get; set; }
        public string StudentAnswer { get; set; } = string.Empty;
        public string ModelAnswer { get; set; } = string.Empty;
        public double MarksAwarded { get; set; }
        public double MaxMarks { get; set; }
        public double ConfidenceScore { get; set; }
        public bool NeedsReview { get; set; }
        public string EvaluationReason { get; set; } = string.Empty;
        public List<string> MatchedKeywords { get; set; } = new();
        public List<string> MissingKeywords { get; set; } = new();
        public List<StepWiseMarks> StepWiseBreakdown { get; set; } = new();
        public Dictionary<string, object> AuditTrail { get; set; } = new();
        public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
        public long ProcessingTimeMs { get; set; }
    }

    /// <summary>
    /// Engine performance statistics
    /// </summary>
    public class EngineStatistics
    {
        public string EngineName { get; set; } = string.Empty;
        public int TotalEvaluations { get; set; }
        public double AverageConfidence { get; set; }
        public double AverageProcessingTimeMs { get; set; }
        public int ReviewCount { get; set; }
        public double AverageScore { get; set; }
    }
}
