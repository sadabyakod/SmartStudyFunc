using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Threading.Tasks;

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
        public async Task<int> InsertUploadedFile(string name, long sizeBytes, string fileType)
        {
            const string sql = @"
                INSERT INTO UploadedFiles (FileName, FileSizeBytes, FileType, UploadedOn)
                OUTPUT INSERTED.Id
                VALUES (@FileName, @FileSizeBytes, @FileType, SYSDATETIME())";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                return await conn.ExecuteScalarAsync<int>(sql, new
                {
                    FileName = name,
                    FileSizeBytes = sizeBytes,
                    FileType = fileType
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
    }
}
