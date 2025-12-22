-- =============================================
-- Migration: Create SubjectiveRubrics Table
-- Purpose: Index table for frozen rubrics stored in blob storage
-- This enables deterministic, fair evaluation of subjective questions
-- 
-- The rubric blob is the CANONICAL source for step-wise marking.
-- This table provides fast lookup by ExamId/QuestionId.
-- =============================================

-- Create SubjectiveRubrics table if not exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SubjectiveRubrics')
BEGIN
    CREATE TABLE SubjectiveRubrics (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        
        -- Exam identification
        ExamId NVARCHAR(100) NOT NULL,
        QuestionId NVARCHAR(50) NOT NULL,
        
        -- Marks (authoritative - overrides GeneratedExams values)
        TotalMarks INT NOT NULL,
        
        -- Blob path to frozen rubric JSON
        -- Format: paper-{ExamId}/question-{QuestionId}.json
        -- Container: modalquestions-rubrics
        RubricBlobPath NVARCHAR(500) NULL,
        
        -- Metadata
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedBy NVARCHAR(100) NULL,
        
        -- Constraints
        CONSTRAINT UQ_SubjectiveRubrics_ExamQuestion UNIQUE (ExamId, QuestionId)
    );
    
    PRINT 'Created table: SubjectiveRubrics';
END
ELSE
BEGIN
    PRINT 'Table SubjectiveRubrics already exists';
END
GO

-- Create index for fast lookup by ExamId
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SubjectiveRubrics_ExamId')
BEGIN
    CREATE INDEX IX_SubjectiveRubrics_ExamId 
    ON SubjectiveRubrics(ExamId);
    
    PRINT 'Created index: IX_SubjectiveRubrics_ExamId';
END
GO

-- Create index for lookup by QuestionId (for cross-exam analysis)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SubjectiveRubrics_QuestionId')
BEGIN
    CREATE INDEX IX_SubjectiveRubrics_QuestionId 
    ON SubjectiveRubrics(QuestionId);
    
    PRINT 'Created index: IX_SubjectiveRubrics_QuestionId';
END
GO

-- =============================================
-- Example Usage:
-- =============================================
-- 
-- INSERT INTO SubjectiveRubrics (ExamId, QuestionId, TotalMarks, RubricBlobPath)
-- VALUES 
--     ('EXAM-20251222-ABC', 'B1', 2, 'paper-EXAM-20251222-ABC/question-B1.json'),
--     ('EXAM-20251222-ABC', 'B2', 2, 'paper-EXAM-20251222-ABC/question-B2.json'),
--     ('EXAM-20251222-ABC', 'C1', 3, 'paper-EXAM-20251222-ABC/question-C1.json');
--
-- The corresponding blob JSON format:
-- {
--   "questionId": "B1",
--   "totalMarks": 2,
--   "rubric": [
--     {"stepNo": 1, "expected": "Apply power rule", "marks": 1},
--     {"stepNo": 2, "expected": "Calculate f'(x) = 2x", "marks": 1}
--   ],
--   "modelAnswer": "Step 1: Apply power rule..."
-- }
-- =============================================

PRINT 'Migration 07_CreateSubjectiveRubricsTable completed successfully';
