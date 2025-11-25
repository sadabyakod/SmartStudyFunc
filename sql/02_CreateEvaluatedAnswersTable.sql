-- ============================================================
-- CREATE TABLE: EvaluatedAnswers
-- Stores AI-evaluated student answers with scores and feedback
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EvaluatedAnswers')
BEGIN
    CREATE TABLE EvaluatedAnswers (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ExamId INT NOT NULL,
        QuestionId INT NOT NULL,
        StudentAnswer NVARCHAR(MAX) NULL,
        ExtractedText NVARCHAR(MAX) NULL,         -- OCR output
        IdealAnswer NVARCHAR(MAX) NULL,           -- From GeneratedQuestions
        Score FLOAT NULL,                          -- AI-assigned score
        MaxMarks INT NOT NULL,
        Feedback NVARCHAR(MAX) NULL,              -- AI feedback
        KeywordsMatched NVARCHAR(500) NULL,       -- Comma-separated matched keywords
        MissingKeywords NVARCHAR(500) NULL,       -- Comma-separated missing keywords
        Strengths NVARCHAR(MAX) NULL,             -- What student did well
        ImprovementSuggestions NVARCHAR(MAX) NULL,-- How to improve
        BlobPath NVARCHAR(500) NULL,              -- Path to uploaded answer image
        EvaluatedOn DATETIME NOT NULL DEFAULT GETDATE(),
        
        -- Foreign Keys
        CONSTRAINT FK_EvaluatedAnswers_Exam 
            FOREIGN KEY (ExamId) REFERENCES GeneratedExams(Id) ON DELETE CASCADE,
        CONSTRAINT FK_EvaluatedAnswers_Question 
            FOREIGN KEY (QuestionId) REFERENCES GeneratedQuestions(Id) ON DELETE NO ACTION
    );

    -- Performance indexes
    CREATE NONCLUSTERED INDEX IX_EvaluatedAnswers_ExamId 
        ON EvaluatedAnswers(ExamId) INCLUDE (Score, MaxMarks);
    
    CREATE NONCLUSTERED INDEX IX_EvaluatedAnswers_QuestionId 
        ON EvaluatedAnswers(QuestionId);
    
    CREATE NONCLUSTERED INDEX IX_EvaluatedAnswers_EvaluatedOn 
        ON EvaluatedAnswers(EvaluatedOn DESC);

    PRINT 'EvaluatedAnswers table created successfully with indexes';
END
ELSE
BEGIN
    PRINT 'EvaluatedAnswers table already exists';
END
GO
