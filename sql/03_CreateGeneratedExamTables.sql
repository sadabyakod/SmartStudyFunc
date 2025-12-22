-- ============================================================================
-- GENERATED EXAM STORAGE SCHEMA
-- ============================================================================
-- This schema stores generated exam papers and questions with blob references
-- for detailed rubrics used in step-wise evaluation.
--
-- Storage Strategy:
--   A) Azure SQL - Exam metadata and question references
--   B) Azure Blob - Detailed rubrics in modalquestions-rubrics container
-- ============================================================================

-- GeneratedExamPapers - Main exam paper metadata
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='GeneratedExamPapers' AND xtype='U')
BEGIN
    CREATE TABLE GeneratedExamPapers (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        PaperId NVARCHAR(100) NOT NULL UNIQUE,       -- Human-readable paper ID like "PAPER-20251222-001"
        ExamId NVARCHAR(100) NOT NULL,               -- Links to GeneratedExams.ExamId
        Seed INT NULL,                                -- Random seed for reproducibility
        Version INT NOT NULL DEFAULT 1,               -- Paper version number
        Subject NVARCHAR(100) NULL,
        Grade NVARCHAR(50) NULL,
        Chapter NVARCHAR(200) NULL,
        TotalMarks INT NOT NULL DEFAULT 0,
        TotalQuestions INT NOT NULL DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(100) NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        
        -- Index for fast lookups
        INDEX IX_GeneratedExamPapers_PaperId (PaperId),
        INDEX IX_GeneratedExamPapers_ExamId (ExamId),
        INDEX IX_GeneratedExamPapers_CreatedAt (CreatedAt DESC)
    );
END
GO

-- GeneratedExamQuestions - Individual questions with rubric blob references
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='GeneratedExamQuestions' AND xtype='U')
BEGIN
    CREATE TABLE GeneratedExamQuestions (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        PaperId NVARCHAR(100) NOT NULL,              -- Foreign key to GeneratedExamPapers.PaperId
        QuestionId NVARCHAR(100) NOT NULL,           -- Unique question ID like "Q1", "Q2", etc.
        QuestionNumber INT NOT NULL,
        Section NVARCHAR(10) NULL,                   -- Part A, B, C, D
        QuestionText NVARCHAR(MAX) NOT NULL,
        QuestionType NVARCHAR(50) NOT NULL DEFAULT 'subjective', -- 'mcq', 'subjective', 'short-answer', 'essay'
        TotalMarks INT NOT NULL DEFAULT 1,
        ModelAnswer NVARCHAR(MAX) NULL,              -- Short model answer
        RubricBlobPath NVARCHAR(500) NULL,           -- Path: modalquestions-rubrics/paper-{PaperId}/question-{QuestionId}.json
        Keywords NVARCHAR(MAX) NULL,                 -- JSON array of keywords
        Topic NVARCHAR(200) NULL,
        McqOptions NVARCHAR(MAX) NULL,               -- JSON array for MCQ options
        CorrectOption NVARCHAR(10) NULL,             -- For MCQ: A, B, C, D
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        IsActive BIT NOT NULL DEFAULT 1,
        
        -- Indexes
        INDEX IX_GeneratedExamQuestions_PaperId (PaperId),
        INDEX IX_GeneratedExamQuestions_QuestionId (QuestionId),
        
        -- Foreign key
        CONSTRAINT FK_GeneratedExamQuestions_Paper 
            FOREIGN KEY (PaperId) REFERENCES GeneratedExamPapers(PaperId) ON DELETE CASCADE
    );
END
GO

-- QuestionRubricSteps - Step-wise marking scheme (optional, for quick queries without blob)
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='QuestionRubricSteps' AND xtype='U')
BEGIN
    CREATE TABLE QuestionRubricSteps (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        QuestionId NVARCHAR(100) NOT NULL,           -- Foreign key to GeneratedExamQuestions.QuestionId
        PaperId NVARCHAR(100) NOT NULL,
        StepNumber INT NOT NULL,
        StepDescription NVARCHAR(500) NOT NULL,
        MaxMarks DECIMAL(5,2) NOT NULL DEFAULT 1,
        Keywords NVARCHAR(MAX) NULL,                 -- JSON array of keywords for this step
        
        -- Indexes
        INDEX IX_QuestionRubricSteps_QuestionId (QuestionId, PaperId),
        
        -- Foreign key
        CONSTRAINT FK_QuestionRubricSteps_Question 
            FOREIGN KEY (QuestionId, PaperId) 
            REFERENCES GeneratedExamQuestions(QuestionId, PaperId) ON DELETE NO ACTION
    );
END
GO

-- Add RubricBlobPath column to existing GeneratedExams table if it exists
IF EXISTS (SELECT * FROM sysobjects WHERE name='GeneratedExams' AND xtype='U')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('GeneratedExams') AND name = 'RubricBlobPath')
    BEGIN
        ALTER TABLE GeneratedExams ADD RubricBlobPath NVARCHAR(500) NULL;
    END
END
GO

PRINT 'Generated Exam tables created successfully';
GO
