-- =====================================================
-- Written Submissions Schema Migration
-- For handwritten answer processing pipeline
-- =====================================================

-- 1. Create WrittenSubmissions table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WrittenSubmissions')
BEGIN
    CREATE TABLE WrittenSubmissions (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        ExamId NVARCHAR(100) NOT NULL,
        StudentId NVARCHAR(100) NOT NULL,
        BlobPaths NVARCHAR(MAX) NOT NULL, -- JSON array of blob paths
        Status INT NOT NULL DEFAULT 0,
        -- 0 = PendingEvaluation
        -- 1 = OcrProcessing
        -- 2 = Evaluating
        -- 3 = Completed
        -- 4 = Failed
        ExtractedText NVARCHAR(MAX) NULL,
        ExtractedTextBlobPath NVARCHAR(500) NULL, -- For large text stored in blob
        ErrorMessage NVARCHAR(2000) NULL,
        TotalScore DECIMAL(10, 2) NULL,
        MaxPossibleScore DECIMAL(10, 2) NULL,
        Percentage DECIMAL(5, 2) NULL,
        RetryCount INT NOT NULL DEFAULT 0,
        BlobsDeleted BIT NOT NULL DEFAULT 0,
        BlobsDeletedAt DATETIME2 NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        OcrCompletedAt DATETIME2 NULL,
        EvaluatedAt DATETIME2 NULL,
        
        INDEX IX_WrittenSubmissions_ExamId (ExamId),
        INDEX IX_WrittenSubmissions_StudentId (StudentId),
        INDEX IX_WrittenSubmissions_Status (Status),
        INDEX IX_WrittenSubmissions_CreatedAt (CreatedAt),
        INDEX IX_WrittenSubmissions_Cleanup (CreatedAt, BlobsDeleted, Status)
    );
    
    PRINT 'Created table: WrittenSubmissions';
END
GO

-- 2. Create WrittenQuestionEvaluations table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'WrittenQuestionEvaluations')
BEGIN
    CREATE TABLE WrittenQuestionEvaluations (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        WrittenSubmissionId UNIQUEIDENTIFIER NOT NULL,
        QuestionId NVARCHAR(100) NOT NULL,
        QuestionNumber INT NOT NULL,
        ExtractedAnswer NVARCHAR(MAX) NOT NULL,
        ModelAnswer NVARCHAR(MAX) NOT NULL,
        MaxScore DECIMAL(10, 2) NOT NULL,
        AwardedScore DECIMAL(10, 2) NOT NULL,
        Feedback NVARCHAR(MAX) NOT NULL,
        RubricBreakdown NVARCHAR(MAX) NULL,
        EvaluatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        
        CONSTRAINT FK_WrittenQuestionEvaluations_Submission 
            FOREIGN KEY (WrittenSubmissionId) 
            REFERENCES WrittenSubmissions(Id) 
            ON DELETE CASCADE,
            
        INDEX IX_WrittenQuestionEvaluations_SubmissionId (WrittenSubmissionId),
        INDEX IX_WrittenQuestionEvaluations_QuestionId (QuestionId)
    );
    
    PRINT 'Created table: WrittenQuestionEvaluations';
END
GO

-- 3. Create ExamQuestions table (if not exists)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ExamQuestions')
BEGIN
    CREATE TABLE ExamQuestions (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        ExamId NVARCHAR(100) NOT NULL,
        QuestionNumber INT NOT NULL,
        QuestionText NVARCHAR(MAX) NOT NULL,
        ModelAnswer NVARCHAR(MAX) NOT NULL,
        MaxScore DECIMAL(10, 2) NOT NULL DEFAULT 10,
        Rubric NVARCHAR(MAX) NULL,
        Keywords NVARCHAR(MAX) NULL, -- JSON array of keywords
        QuestionType NVARCHAR(50) NULL, -- 'essay', 'short-answer', 'calculation'
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        
        INDEX IX_ExamQuestions_ExamId (ExamId),
        CONSTRAINT UQ_ExamQuestions_ExamQuestion UNIQUE (ExamId, QuestionNumber)
    );
    
    PRINT 'Created table: ExamQuestions';
END
GO

-- 4. Add indexes for performance optimization
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WrittenSubmissions_ExamStudent')
BEGIN
    CREATE INDEX IX_WrittenSubmissions_ExamStudent 
    ON WrittenSubmissions(ExamId, StudentId);
    
    PRINT 'Created index: IX_WrittenSubmissions_ExamStudent';
END
GO

-- 5. Create view for submission status dashboard
IF EXISTS (SELECT * FROM sys.views WHERE name = 'vw_WrittenSubmissionStatus')
    DROP VIEW vw_WrittenSubmissionStatus;
GO

CREATE VIEW vw_WrittenSubmissionStatus AS
SELECT 
    ws.Id,
    ws.ExamId,
    ws.StudentId,
    CASE ws.Status
        WHEN 0 THEN 'PendingEvaluation'
        WHEN 1 THEN 'OcrProcessing'
        WHEN 2 THEN 'Evaluating'
        WHEN 3 THEN 'Completed'
        WHEN 4 THEN 'Failed'
    END AS StatusName,
    ws.TotalScore,
    ws.MaxPossibleScore,
    ws.Percentage,
    ws.RetryCount,
    ws.ErrorMessage,
    ws.CreatedAt,
    ws.OcrCompletedAt,
    ws.EvaluatedAt,
    DATEDIFF(SECOND, ws.CreatedAt, ws.OcrCompletedAt) AS OcrDurationSeconds,
    DATEDIFF(SECOND, ws.OcrCompletedAt, ws.EvaluatedAt) AS EvaluationDurationSeconds,
    (SELECT COUNT(*) FROM WrittenQuestionEvaluations wqe WHERE wqe.WrittenSubmissionId = ws.Id) AS QuestionCount
FROM WrittenSubmissions ws;
GO

PRINT 'Created view: vw_WrittenSubmissionStatus';
GO

-- 6. Create stored procedure for queue message generation
IF EXISTS (SELECT * FROM sys.procedures WHERE name = 'sp_GetPendingWrittenSubmissions')
    DROP PROCEDURE sp_GetPendingWrittenSubmissions;
GO

CREATE PROCEDURE sp_GetPendingWrittenSubmissions
    @BatchSize INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT TOP (@BatchSize)
        Id AS WrittenSubmissionId,
        ExamId,
        StudentId,
        BlobPaths
    FROM WrittenSubmissions
    WHERE Status = 0 -- PendingEvaluation
    ORDER BY CreatedAt ASC;
END;
GO

PRINT 'Created stored procedure: sp_GetPendingWrittenSubmissions';
GO

PRINT '========================================';
PRINT 'Written Submissions Schema Migration Complete';
PRINT '========================================';
