-- Fix UploadedFiles table schema to match code expectations
-- Run this script against your Azure SQL Database

-- First, check if columns exist and add them if they don't
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'FileName')
BEGIN
    ALTER TABLE [UploadedFiles] ADD [FileName] NVARCHAR(255) NOT NULL DEFAULT '';
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'FileSizeBytes')
BEGIN
    ALTER TABLE [UploadedFiles] ADD [FileSizeBytes] BIGINT NOT NULL DEFAULT 0;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'FileType')
BEGIN
    ALTER TABLE [UploadedFiles] ADD [FileType] NVARCHAR(50) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'ClassName')
BEGIN
    ALTER TABLE [UploadedFiles] ADD [ClassName] NVARCHAR(100) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'Subject')
BEGIN
    ALTER TABLE [UploadedFiles] ADD [Subject] NVARCHAR(100) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'Chapter')
BEGIN
    ALTER TABLE [UploadedFiles] ADD [Chapter] NVARCHAR(200) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'UploadedOn')
BEGIN
    ALTER TABLE [UploadedFiles] ADD [UploadedOn] DATETIME2 NOT NULL DEFAULT SYSDATETIME();
END

-- Also ensure Id column exists as primary key
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('UploadedFiles') AND name = 'Id')
BEGIN
    -- If the table structure is completely wrong, you may need to recreate it
    PRINT 'WARNING: Id column does not exist. Table structure may need to be recreated.';
END
