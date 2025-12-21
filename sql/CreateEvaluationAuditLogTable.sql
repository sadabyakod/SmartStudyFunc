-- =====================================================
-- PRODUCTION-GRADE: Evaluation Audit Log Table
-- Stores complete evaluation history for compliance
-- =====================================================

-- Create EvaluationAuditLog table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[EvaluationAuditLog]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[EvaluationAuditLog] (
        -- Primary Key
        [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
        [EvaluationId] NVARCHAR(50) NOT NULL UNIQUE,
        
        -- Question/Exam Context
        [QuestionId] NVARCHAR(50) NOT NULL,
        [ExamId] INT NOT NULL,
        [UserId] INT NOT NULL,
        
        -- Classification
        [EngineName] NVARCHAR(100) NOT NULL,
        [SubjectCategory] NVARCHAR(50) NOT NULL,
        [QuestionType] NVARCHAR(50) NOT NULL,
        [ClassLevel] INT NOT NULL,
        
        -- Answer Data
        [StudentAnswer] NVARCHAR(MAX) NOT NULL,
        [ModelAnswer] NVARCHAR(MAX) NOT NULL,
        
        -- Evaluation Results
        [MarksAwarded] DECIMAL(5,2) NOT NULL,
        [MaxMarks] DECIMAL(5,2) NOT NULL,
        [ConfidenceScore] DECIMAL(3,2) NOT NULL,
        [NeedsReview] BIT NOT NULL DEFAULT 0,
        [EvaluationReason] NVARCHAR(MAX) NOT NULL,
        
        -- Detailed Analysis (JSON)
        [MatchedKeywords] NVARCHAR(MAX), -- JSON array
        [MissingKeywords] NVARCHAR(MAX), -- JSON array
        [StepWiseBreakdown] NVARCHAR(MAX), -- JSON array
        [AuditTrail] NVARCHAR(MAX), -- JSON object with full trace
        
        -- Metadata
        [EvaluatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [ProcessingTimeMs] BIGINT NOT NULL DEFAULT 0,
        
        -- Teacher Override (future enhancement)
        [TeacherOverrideMarks] DECIMAL(5,2) NULL,
        [TeacherOverrideReason] NVARCHAR(MAX) NULL,
        [TeacherOverrideBy] INT NULL,
        [TeacherOverrideAt] DATETIME2 NULL,
        
        CONSTRAINT [CK_EvaluationAuditLog_Confidence] CHECK ([ConfidenceScore] >= 0 AND [ConfidenceScore] <= 1),
        CONSTRAINT [CK_EvaluationAuditLog_Marks] CHECK ([MarksAwarded] >= 0 AND [MarksAwarded] <= [MaxMarks])
    );
    
    PRINT 'Created table: EvaluationAuditLog';
END
GO

-- Indexes for performance
CREATE NONCLUSTERED INDEX [IX_EvaluationAuditLog_ExamId] 
    ON [dbo].[EvaluationAuditLog] ([ExamId]) 
    INCLUDE ([QuestionId], [UserId], [MarksAwarded], [ConfidenceScore]);

CREATE NONCLUSTERED INDEX [IX_EvaluationAuditLog_QuestionId] 
    ON [dbo].[EvaluationAuditLog] ([QuestionId]);

CREATE NONCLUSTERED INDEX [IX_EvaluationAuditLog_UserId] 
    ON [dbo].[EvaluationAuditLog] ([UserId]);

CREATE NONCLUSTERED INDEX [IX_EvaluationAuditLog_NeedsReview] 
    ON [dbo].[EvaluationAuditLog] ([NeedsReview], [ConfidenceScore]) 
    WHERE [NeedsReview] = 1;

CREATE NONCLUSTERED INDEX [IX_EvaluationAuditLog_EngineName] 
    ON [dbo].[EvaluationAuditLog] ([EngineName]) 
    INCLUDE ([ConfidenceScore], [ProcessingTimeMs]);

CREATE NONCLUSTERED INDEX [IX_EvaluationAuditLog_EvaluatedAt] 
    ON [dbo].[EvaluationAuditLog] ([EvaluatedAt] DESC);

PRINT 'Created indexes on EvaluationAuditLog';
GO

-- =====================================================
-- Sample Query: Get evaluations needing review
-- =====================================================
-- SELECT 
--     EvaluationId,
--     QuestionId,
--     EngineName,
--     MarksAwarded,
--     MaxMarks,
--     ConfidenceScore,
--     EvaluationReason,
--     EvaluatedAt
-- FROM EvaluationAuditLog
-- WHERE NeedsReview = 1
--    OR ConfidenceScore < 0.7
-- ORDER BY ConfidenceScore ASC, EvaluatedAt DESC;

-- =====================================================
-- Sample Query: Engine performance statistics
-- =====================================================
-- SELECT 
--     EngineName,
--     COUNT(*) as TotalEvaluations,
--     AVG(ConfidenceScore) as AvgConfidence,
--     AVG(ProcessingTimeMs) as AvgProcessingTime,
--     SUM(CASE WHEN NeedsReview = 1 THEN 1 ELSE 0 END) as ReviewCount,
--     AVG(MarksAwarded / NULLIF(MaxMarks, 0)) as AvgScore
-- FROM EvaluationAuditLog
-- WHERE EvaluatedAt >= DATEADD(day, -7, GETUTCDATE())
-- GROUP BY EngineName;

-- =====================================================
-- Sample Query: Audit trail for specific evaluation
-- =====================================================
-- SELECT 
--     *
-- FROM EvaluationAuditLog
-- WHERE EvaluationId = 'EVL-12345-ABCD';
