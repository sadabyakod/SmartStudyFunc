using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SmartStudyFunc.Models;

namespace SmartStudyFunc
{
    public class SqlDb
    {
        private readonly string _connectionString;

        public SqlDb(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString));

            _connectionString = connectionString;
        }

        // -----------------------------
        // Insert Uploaded File
        // -----------------------------
        public async Task<int> InsertUploadedFile(
            string name, 
            long sizeBytes, 
            string fileType,
            string? className = null,
            string? subject = null,
            string? chapter = null)
        {
            const string sql = @"
                INSERT INTO UploadedFiles (FileName, FileSizeBytes, FileType, ClassName, Subject, Chapter, UploadedOn)
                OUTPUT INSERTED.Id
                VALUES (@FileName, @FileSizeBytes, @FileType, @ClassName, @Subject, @Chapter, SYSDATETIME())";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                return await conn.ExecuteScalarAsync<int>(sql, new
                {
                    FileName = name,
                    FileSizeBytes = sizeBytes,
                    FileType = fileType,
                    ClassName = className,
                    Subject = subject,
                    Chapter = chapter
                });
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"InsertUploadedFile failed: {ex.Message}", ex);
            }
        }

        // -----------------------------
        // Insert Chunk
        // -----------------------------
        public async Task<int> InsertChunk(
            int uploadedFileId,
            string topicTitle,
            string summary,
            string chunkText,
            int tokenCount,
            int pageFrom,
            int pageTo,
            string chunkType)
        {
            const string sql = @"
                INSERT INTO FileChunks 
                (UploadedFileId, TopicTitle, Summary, ChunkText, TokenCount, PageFrom, PageTo, ChunkType, CreatedOn)
                OUTPUT INSERTED.Id
                VALUES 
                (@UploadedFileId, @TopicTitle, @Summary, @ChunkText, @TokenCount, @PageFrom, @PageTo, @ChunkType, SYSDATETIME())";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                return await conn.ExecuteScalarAsync<int>(sql, new
                {
                    UploadedFileId = uploadedFileId,
                    TopicTitle = topicTitle,
                    Summary = summary,
                    ChunkText = chunkText,
                    TokenCount = tokenCount,
                    PageFrom = pageFrom,
                    PageTo = pageTo,
                    ChunkType = chunkType
                });
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"InsertChunk failed: {ex.Message}", ex);
            }
        }

        // -----------------------------
        // Insert Embedding
        // -----------------------------
        public async Task InsertEmbedding(int chunkId, byte[] embedding)
        {
            const string sql = @"
                INSERT INTO ChunkEmbeddings (ChunkId, Embedding, CreatedOn)
                VALUES (@ChunkId, @Embedding, SYSDATETIME())";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                await conn.ExecuteAsync(sql, new
                {
                    ChunkId = chunkId,
                    Embedding = embedding
                });
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"InsertEmbedding failed: {ex.Message}", ex);
            }
        }

        // -----------------------------
        // Get All Chunks with Embeddings
        // Used for RAG search - retrieves all chunks and embeddings to compute similarity in C#
        // -----------------------------
        public async Task<List<SearchResultChunk>> GetAllChunksWithEmbeddings()
        {
            const string sql = @"
                SELECT 
                    fc.Id AS ChunkId,
                    fc.ChunkText,
                    fc.UploadedFileId AS FileId,
                    0 AS ChunkIndex,
                    ce.Embedding
                FROM FileChunks fc
                INNER JOIN ChunkEmbeddings ce ON fc.Id = ce.ChunkId
                WHERE ce.Embedding IS NOT NULL";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                var results = await conn.QueryAsync<SearchResultChunk>(sql);
                return results.ToList();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"GetAllChunksWithEmbeddings failed: {ex.Message}", ex);
            }
        }

        // -----------------------------
        // Insert RAG Search Log
        // Logs search queries and results for analytics
        // -----------------------------
        public async Task<int> InsertRagSearchLog(
            string question,
            string answer,
            string retrievedChunkIds,
            double confidenceScore)
        {
            const string sql = @"
                INSERT INTO RAGSearchLogs 
                (QuestionId, Question, Answer, RetrievedChunkIds, ConfidenceScore, CreatedOn)
                OUTPUT INSERTED.Id
                VALUES 
                (NULL, @Question, @Answer, @RetrievedChunkIds, @ConfidenceScore, SYSDATETIME())";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                return await conn.ExecuteScalarAsync<int>(sql, new
                {
                    Question = question,
                    Answer = answer,
                    RetrievedChunkIds = retrievedChunkIds,
                    ConfidenceScore = confidenceScore
                });
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException($"InsertRagSearchLog failed: {ex.Message}", ex);
            }
        }
    }
}
