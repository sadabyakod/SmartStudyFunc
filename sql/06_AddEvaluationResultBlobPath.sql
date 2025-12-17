-- ============================================================================
-- Migration: Add EvaluationResultBlobPath column to WrittenSubmissions table
-- Purpose: Store permanent evaluation results in blob storage for student access
-- Date: 2025-12-15
-- ============================================================================

-- Check if column already exists before adding
IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.WrittenSubmissions') 
    AND name = 'EvaluationResultBlobPath'
)
BEGIN
    ALTER TABLE WrittenSubmissions
    ADD EvaluationResultBlobPath NVARCHAR(500) NULL;
    
    PRINT 'Column EvaluationResultBlobPath added successfully';
END
ELSE
BEGIN
    PRINT 'Column EvaluationResultBlobPath already exists';
END

GO

-- Add index for faster lookups when retrieving results
IF NOT EXISTS (
    SELECT * FROM sys.indexes 
    WHERE name = 'IX_WrittenSubmissions_EvaluationResultBlobPath' 
    AND object_id = OBJECT_ID('dbo.WrittenSubmissions')
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_WrittenSubmissions_EvaluationResultBlobPath
    ON WrittenSubmissions(EvaluationResultBlobPath)
    WHERE EvaluationResultBlobPath IS NOT NULL;
    
    PRINT 'Index created successfully';
END
ELSE
BEGIN
    PRINT 'Index already exists';
END

GO

PRINT 'Migration completed successfully';
