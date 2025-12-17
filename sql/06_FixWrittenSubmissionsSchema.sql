-- =============================================
-- Fix WrittenSubmissions Schema
-- Adds missing columns to match code expectations
-- =============================================

PRINT 'Starting schema fix for WrittenSubmissions...';
GO

-- Rename CreatedAt to SubmittedAt if it exists
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'CreatedAt')
   AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'SubmittedAt')
BEGIN
    EXEC sp_rename 'WrittenSubmissions.CreatedAt', 'SubmittedAt', 'COLUMN';
    PRINT 'Renamed CreatedAt to SubmittedAt';
END
GO

-- Rename BlobPaths to FilePaths if it exists
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'BlobPaths')
   AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'FilePaths')
BEGIN
    EXEC sp_rename 'WrittenSubmissions.BlobPaths', 'FilePaths', 'COLUMN';
    PRINT 'Renamed BlobPaths to FilePaths';
END
GO

-- Add OcrStartedAt if missing
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'OcrStartedAt')
BEGIN
    ALTER TABLE WrittenSubmissions ADD OcrStartedAt DATETIME2 NULL;
    PRINT 'Added column: OcrStartedAt';
END
GO

-- Add EvaluationStartedAt if missing
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'EvaluationStartedAt')
BEGIN
    ALTER TABLE WrittenSubmissions ADD EvaluationStartedAt DATETIME2 NULL;
    PRINT 'Added column: EvaluationStartedAt';
END
GO

-- Add ExtractedTextJson if missing
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'ExtractedTextJson')
BEGIN
    ALTER TABLE WrittenSubmissions ADD ExtractedTextJson NVARCHAR(MAX) NULL;
    PRINT 'Added column: ExtractedTextJson';
END
GO

-- Add OcrProcessingTimeMs if missing
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'OcrProcessingTimeMs')
BEGIN
    ALTER TABLE WrittenSubmissions ADD OcrProcessingTimeMs BIGINT NULL;
    PRINT 'Added column: OcrProcessingTimeMs';
END
GO

-- Add EvaluationProcessingTimeMs if missing
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'EvaluationProcessingTimeMs')
BEGIN
    ALTER TABLE WrittenSubmissions ADD EvaluationProcessingTimeMs BIGINT NULL;
    PRINT 'Added column: EvaluationProcessingTimeMs';
END
GO

-- Add Grade if missing
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WrittenSubmissions') AND name = 'Grade')
BEGIN
    ALTER TABLE WrittenSubmissions ADD Grade NVARCHAR(10) NULL;
    PRINT 'Added column: Grade';
END
GO

-- Verify final schema
PRINT '';
PRINT 'Final WrittenSubmissions columns:';
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WrittenSubmissions'
ORDER BY ORDINAL_POSITION;
GO

PRINT '';
PRINT '=============================================';
PRINT 'Schema fix completed successfully!';
PRINT '=============================================';
GO
