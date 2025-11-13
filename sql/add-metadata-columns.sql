-- =============================================
-- Migration: Add Metadata Columns to UploadedFiles
-- Description: Adds ClassName, Subject, and Chapter columns to support organized textbook uploads
-- Date: 2025-11-13
-- =============================================

-- Add new columns to UploadedFiles table
ALTER TABLE UploadedFiles
ADD 
    ClassName NVARCHAR(100) NULL,
    Subject NVARCHAR(100) NULL,
    Chapter NVARCHAR(100) NULL;

GO

-- Create index for better query performance on metadata fields
CREATE NONCLUSTERED INDEX IX_UploadedFiles_Metadata
ON UploadedFiles (ClassName, Subject, Chapter)
INCLUDE (FileName, UploadedOn);

GO

-- Verify the changes
SELECT 
    COLUMN_NAME, 
    DATA_TYPE, 
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'UploadedFiles'
ORDER BY ORDINAL_POSITION;

GO

PRINT 'Migration completed successfully. UploadedFiles table now supports className, subject, and chapter metadata.';
