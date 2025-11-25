-- ============================================================
-- Create EvaluatedAnswers Table
-- Stores student answers and AI evaluation results
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EvaluatedAnswers')
BEGIN
    CREATE TABLE EvaluatedAnswers (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ExamId INT NOT NULL,
        QuestionId INT NOT NULL,
        StudentAnswer NVARCHAR(MAX) NULL,
        ExtractedText NVARCHAR(MAX) NULL, -- OCR output
        IdealAnswer NVARCHAR(MAX) NULL,
        Score FLOAT NOT NULL DEFAULT 0,
        MaxMarks INT NOT NULL,
        Feedback NVARCHAR(MAX) NULL,
        KeywordsMatched NVARCHAR(500) NULL,
        MissingKeywords NVARCHAR(500) NULL,
        Strengths NVARCHAR(MAX) NULL,
        ImprovementSuggestions NVARCHAR(MAX) NULL,
        ImageBlobPath NVARCHAR(500) NULL, -- Path to uploaded answer image/PDF
        CreatedOn DATETIME NOT NULL DEFAULT GETDATE(),
        UpdatedOn DATETIME NULL,
        
        -- Foreign Keys
        CONSTRAINT FK_EvaluatedAnswers_Exams FOREIGN KEY (ExamId) 
            REFERENCES Exams(Id) ON DELETE CASCADE,
        CONSTRAINT FK_EvaluatedAnswers_Questions FOREIGN KEY (QuestionId) 
            REFERENCES GeneratedQuestions(Id) ON DELETE NO ACTION
    );

    -- Indexes for performance
    CREATE INDEX IX_EvaluatedAnswers_ExamId ON EvaluatedAnswers(ExamId);
    CREATE INDEX IX_EvaluatedAnswers_QuestionId ON EvaluatedAnswers(QuestionId);
    CREATE INDEX IX_EvaluatedAnswers_CreatedOn ON EvaluatedAnswers(CreatedOn DESC);

    PRINT 'EvaluatedAnswers table created successfully';
END
ELSE
BEGIN
    PRINT 'EvaluatedAnswers table already exists';
END
GO
