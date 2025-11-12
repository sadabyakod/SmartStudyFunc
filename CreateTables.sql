-- Create UploadedFiles table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='UploadedFiles' and xtype='U')
BEGIN
    CREATE TABLE UploadedFiles (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        FileName NVARCHAR(500) NOT NULL,
        FileSize BIGINT NOT NULL,
        FileExtension NVARCHAR(50),
        UploadedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
    );
END
GO

-- Create FileChunks table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FileChunks' and xtype='U')
BEGIN
    CREATE TABLE FileChunks (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        UploadedFileId INT NOT NULL,
        TopicTitle NVARCHAR(500),
        ChunkText NVARCHAR(MAX) NOT NULL,
        TokenCount INT NOT NULL,
        PageFrom INT DEFAULT 0,
        PageTo INT DEFAULT 0,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (UploadedFileId) REFERENCES UploadedFiles(Id) ON DELETE CASCADE
    );
END
GO

-- Create ChunkEmbeddings table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ChunkEmbeddings' and xtype='U')
BEGIN
    CREATE TABLE ChunkEmbeddings (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ChunkId INT NOT NULL,
        EmbeddingVector VARBINARY(MAX) NOT NULL,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        FOREIGN KEY (ChunkId) REFERENCES FileChunks(Id) ON DELETE CASCADE
    );
END
GO

-- Create indexes for better performance
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='IX_FileChunks_UploadedFileId')
BEGIN
    CREATE INDEX IX_FileChunks_UploadedFileId ON FileChunks(UploadedFileId);
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='IX_ChunkEmbeddings_ChunkId')
BEGIN
    CREATE INDEX IX_ChunkEmbeddings_ChunkId ON ChunkEmbeddings(ChunkId);
END
GO
