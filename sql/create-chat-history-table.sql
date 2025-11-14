-- Chat History Table for conversation context
CREATE TABLE ChatHistory (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ConversationId UNIQUEIDENTIFIER NOT NULL,
    Role NVARCHAR(20) NOT NULL, -- 'user' or 'assistant'
    Message NVARCHAR(MAX) NOT NULL,
    ChunksUsed NVARCHAR(MAX) NULL, -- JSON array of chunk IDs
    Confidence FLOAT NULL,
    CreatedOn DATETIME2 DEFAULT GETUTCDATE(),
    INDEX IX_ConversationId (ConversationId, CreatedOn)
);
GO

-- Index for quick conversation retrieval
CREATE INDEX IX_ChatHistory_Conversation_Date 
ON ChatHistory (ConversationId, CreatedOn DESC);
GO
