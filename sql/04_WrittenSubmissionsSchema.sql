-- =============================================
-- Written Submissions Tables Migration
-- SmartStudy Functions - Production Schema
-- =============================================
-- Run this script against your Azure SQL Database
-- =============================================

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- =============================================
-- Table: WrittenSubmissions
-- Stores handwritten answer submission data
-- Status flow: Uploaded(0) → OcrProcessing(1) → Evaluating(2) → Completed(3) | Failed(4)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WrittenSubmissions]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[WrittenSubmissions] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [ExamId] NVARCHAR(100) NOT NULL,
        [StudentId] NVARCHAR(100) NOT NULL,
        [FilePaths] NVARCHAR(MAX) NOT NULL, -- JSON array of blob paths
        [Status] INT NOT NULL DEFAULT 0,     -- 0=Uploaded, 1=OcrProcessing, 2=Evaluating, 3=Completed, 4=Failed
        
        -- OCR Results
        [ExtractedText] NVARCHAR(MAX) NULL,           -- Combined normalized text
        [ExtractedTextJson] NVARCHAR(MAX) NULL,       -- JSON with page-by-page results
        [ExtractedTextBlobPath] NVARCHAR(500) NULL,   -- Blob path for large text
        
        -- Evaluation Results
        [TotalScore] DECIMAL(10,2) NULL,
        [MaxPossibleScore] DECIMAL(10,2) NULL,
        [Percentage] DECIMAL(5,2) NULL,
        [Grade] NVARCHAR(10) NULL,
        
        -- Error Tracking
        [ErrorMessage] NVARCHAR(MAX) NULL,
        [RetryCount] INT NOT NULL DEFAULT 0,
        
        -- Timestamps
        [SubmittedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [OcrStartedAt] DATETIME2 NULL,
        [OcrCompletedAt] DATETIME2 NULL,
        [EvaluationStartedAt] DATETIME2 NULL,
        [EvaluatedAt] DATETIME2 NULL,
        
        -- Performance Metrics
        [OcrProcessingTimeMs] BIGINT NULL,
        [EvaluationProcessingTimeMs] BIGINT NULL,
        
        -- Cleanup Tracking
        [BlobsDeleted] BIT NOT NULL DEFAULT 0,
        [BlobsDeletedAt] DATETIME2 NULL,
        
        -- Indexing
        INDEX IX_WrittenSubmissions_ExamId NONCLUSTERED ([ExamId]),
        INDEX IX_WrittenSubmissions_StudentId NONCLUSTERED ([StudentId]),
        INDEX IX_WrittenSubmissions_Status NONCLUSTERED ([Status]),
        INDEX IX_WrittenSubmissions_SubmittedAt NONCLUSTERED ([SubmittedAt])
    );
    
    PRINT 'Created table: WrittenSubmissions';
END
GO

-- =============================================
-- Table: WrittenQuestionEvaluations
-- Stores per-question evaluation results
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[WrittenQuestionEvaluations]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[WrittenQuestionEvaluations] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [WrittenSubmissionId] UNIQUEIDENTIFIER NOT NULL,
        [QuestionId] NVARCHAR(100) NOT NULL,
        [QuestionNumber] INT NOT NULL,
        [ExtractedAnswer] NVARCHAR(MAX) NOT NULL,     -- Student's extracted answer
        [ModelAnswer] NVARCHAR(MAX) NOT NULL,         -- Expected model answer
        [MaxScore] DECIMAL(10,2) NOT NULL,
        [AwardedScore] DECIMAL(10,2) NOT NULL,
        [Feedback] NVARCHAR(MAX) NOT NULL,            -- AI-generated feedback
        [RubricBreakdown] NVARCHAR(MAX) NULL,         -- JSON with per-criterion scores
        [Strengths] NVARCHAR(MAX) NULL,               -- JSON array of strengths
        [Improvements] NVARCHAR(MAX) NULL,            -- JSON array of areas for improvement
        [Confidence] DECIMAL(5,4) NULL,               -- AI confidence score 0.0-1.0
        [EvaluatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        
        CONSTRAINT FK_WrittenQuestionEvaluations_Submission 
            FOREIGN KEY ([WrittenSubmissionId]) 
            REFERENCES [dbo].[WrittenSubmissions]([Id]) 
            ON DELETE CASCADE,
            
        INDEX IX_WrittenQuestionEvaluations_SubmissionId NONCLUSTERED ([WrittenSubmissionId]),
        INDEX IX_WrittenQuestionEvaluations_QuestionId NONCLUSTERED ([QuestionId])
    );
    
    PRINT 'Created table: WrittenQuestionEvaluations';
END
GO

-- =============================================
-- Table: ExamQuestions
-- Stores exam questions with rubrics for AI evaluation
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ExamQuestions]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[ExamQuestions] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [ExamId] NVARCHAR(100) NOT NULL,
        [QuestionNumber] INT NOT NULL,
        [QuestionText] NVARCHAR(MAX) NOT NULL,
        [ModelAnswer] NVARCHAR(MAX) NOT NULL,
        [MaxScore] DECIMAL(10,2) NOT NULL,
        [Rubric] NVARCHAR(MAX) NULL,                  -- Detailed marking rubric
        [Keywords] NVARCHAR(MAX) NULL,                -- JSON array of expected keywords
        [PartialCreditRules] NVARCHAR(MAX) NULL,      -- JSON with partial credit rules
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt] DATETIME2 NULL,
        
        CONSTRAINT UQ_ExamQuestions_ExamQuestion UNIQUE ([ExamId], [QuestionNumber]),
        
        INDEX IX_ExamQuestions_ExamId NONCLUSTERED ([ExamId])
    );
    
    PRINT 'Created table: ExamQuestions';
END
GO

-- =============================================
-- View: vw_WrittenSubmissionStatus
-- Provides a summary view of submission statuses
-- =============================================
IF EXISTS (SELECT * FROM sys.views WHERE object_id = OBJECT_ID(N'[dbo].[vw_WrittenSubmissionStatus]'))
    DROP VIEW [dbo].[vw_WrittenSubmissionStatus];
GO

CREATE VIEW [dbo].[vw_WrittenSubmissionStatus] AS
SELECT 
    ws.Id,
    ws.ExamId,
    ws.StudentId,
    CASE ws.Status
        WHEN 0 THEN 'Uploaded'
        WHEN 1 THEN 'OcrProcessing'
        WHEN 2 THEN 'Evaluating'
        WHEN 3 THEN 'Completed'
        WHEN 4 THEN 'Failed'
        ELSE 'Unknown'
    END AS StatusName,
    ws.TotalScore,
    ws.MaxPossibleScore,
    ws.Percentage,
    ws.Grade,
    ws.SubmittedAt,
    ws.EvaluatedAt,
    ws.RetryCount,
    ws.OcrProcessingTimeMs,
    ws.EvaluationProcessingTimeMs,
    (ws.OcrProcessingTimeMs + ISNULL(ws.EvaluationProcessingTimeMs, 0)) AS TotalProcessingTimeMs,
    DATEDIFF(SECOND, ws.SubmittedAt, ISNULL(ws.EvaluatedAt, GETUTCDATE())) AS TotalDurationSeconds,
    (SELECT COUNT(*) FROM WrittenQuestionEvaluations wqe WHERE wqe.WrittenSubmissionId = ws.Id) AS QuestionsEvaluated
FROM WrittenSubmissions ws;
GO

PRINT 'Created view: vw_WrittenSubmissionStatus';
GO

-- =============================================
-- Stored Procedure: sp_GetPendingWrittenSubmissions
-- Returns submissions that may need reprocessing
-- =============================================
IF EXISTS (SELECT * FROM sys.procedures WHERE object_id = OBJECT_ID(N'[dbo].[sp_GetPendingWrittenSubmissions]'))
    DROP PROCEDURE [dbo].[sp_GetPendingWrittenSubmissions];
GO

CREATE PROCEDURE [dbo].[sp_GetPendingWrittenSubmissions]
    @StuckThresholdMinutes INT = 30,
    @MaxRetryCount INT = 3
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Find submissions that are stuck in processing state
    SELECT 
        Id,
        ExamId,
        StudentId,
        Status,
        RetryCount,
        SubmittedAt,
        OcrStartedAt,
        EvaluationStartedAt,
        ErrorMessage
    FROM WrittenSubmissions
    WHERE 
        -- Stuck in OcrProcessing
        (Status = 1 AND OcrStartedAt < DATEADD(MINUTE, -@StuckThresholdMinutes, GETUTCDATE()))
        OR
        -- Stuck in Evaluating  
        (Status = 2 AND EvaluationStartedAt < DATEADD(MINUTE, -@StuckThresholdMinutes, GETUTCDATE()))
        AND
        -- Under retry limit
        RetryCount < @MaxRetryCount
    ORDER BY SubmittedAt ASC;
END
GO

PRINT 'Created stored procedure: sp_GetPendingWrittenSubmissions';
GO

-- =============================================
-- Insert sample exam questions for testing
-- =============================================
IF NOT EXISTS (SELECT 1 FROM ExamQuestions WHERE ExamId = 'SAMPLE-EXAM-001')
BEGIN
    INSERT INTO ExamQuestions (Id, ExamId, QuestionNumber, QuestionText, ModelAnswer, MaxScore, Rubric, Keywords)
    VALUES 
    (NEWID(), 'SAMPLE-EXAM-001', 1, 
     'Explain the process of photosynthesis in plants.',
     'Photosynthesis is the process by which green plants convert light energy into chemical energy. It occurs in chloroplasts using chlorophyll. The process involves two stages: light-dependent reactions (in thylakoids) and light-independent reactions (Calvin cycle in stroma). Water and carbon dioxide are converted into glucose and oxygen.',
     20,
     'Full marks (20): Complete explanation with both stages, location, reactants and products. Partial (15): Missing one component. Partial (10): Basic understanding only. Minimal (5): Mentions photosynthesis converts light to energy.',
     '["chloroplast","chlorophyll","light-dependent","Calvin cycle","glucose","oxygen","carbon dioxide","thylakoid","stroma"]'),
    
    (NEWID(), 'SAMPLE-EXAM-001', 2,
     'What are the three laws of motion proposed by Newton?',
     'First Law (Inertia): An object at rest stays at rest, and an object in motion stays in motion unless acted upon by an external force. Second Law: Force equals mass times acceleration (F=ma). Third Law: For every action, there is an equal and opposite reaction.',
     15,
     'Full marks (15): All three laws correctly stated with explanations. Partial (10): Two laws correct. Partial (5): One law correct.',
     '["inertia","F=ma","force","mass","acceleration","action","reaction","external force"]'),
    
    (NEWID(), 'SAMPLE-EXAM-001', 3,
     'Describe the water cycle and its importance.',
     'The water cycle involves evaporation (water to vapor), condensation (vapor to clouds), precipitation (rain/snow), and collection (water bodies). It is important for distributing fresh water, regulating climate, and supporting all life on Earth.',
     15,
     'Full marks (15): All four stages explained with importance. Partial (10): Three stages with some importance. Partial (5): Basic understanding.',
     '["evaporation","condensation","precipitation","collection","climate","fresh water"]');
    
    PRINT 'Inserted sample exam questions for SAMPLE-EXAM-001';
END
GO

PRINT '=============================================';
PRINT 'Migration completed successfully!';
PRINT '=============================================';
GO
