-- SQL migration script to add missing columns to UploadedFiles table
ALTER TABLE [UploadedFiles]
ADD [BlobUrl] NVARCHAR(500),
    [Grade] NVARCHAR(50),
    [Status] NVARCHAR(50),
    [TotalChunks] INT,
    [UploadedAt] DATETIME2,
    [UploadedBy] NVARCHAR(200);
