-- =============================================
-- Migration Script: Add Syllabus Metadata to ExamQuestions
-- Description: Adds ClassName, Subject, and Chapter columns to ExamQuestions
--              for RAG-based syllabus retrieval in step-wise evaluation
-- =============================================

PRINT 'Starting migration: Add syllabus metadata to ExamQuestions...';

-- Add ClassName column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ExamQuestions') AND name = 'ClassName')
BEGIN
    ALTER TABLE [ExamQuestions] ADD [ClassName] NVARCHAR(100) NULL;
    PRINT 'Added column: ClassName';
END
GO

-- Add Subject column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ExamQuestions') AND name = 'Subject')
BEGIN
    ALTER TABLE [ExamQuestions] ADD [Subject] NVARCHAR(100) NULL;
    PRINT 'Added column: Subject';
END
GO

-- Add Chapter column
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ExamQuestions') AND name = 'Chapter')
BEGIN
    ALTER TABLE [ExamQuestions] ADD [Chapter] NVARCHAR(200) NULL;
    PRINT 'Added column: Chapter';
END
GO

-- Create composite index for syllabus lookup
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_ExamQuestions_Syllabus' AND object_id = OBJECT_ID('ExamQuestions'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_ExamQuestions_Syllabus
    ON [ExamQuestions] ([ClassName], [Subject], [Chapter]);
    PRINT 'Created index: IX_ExamQuestions_Syllabus';
END
GO

PRINT 'Migration completed: ExamQuestions now supports syllabus metadata for step-wise RAG evaluation.';
GO
