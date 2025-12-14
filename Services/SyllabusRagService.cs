using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using SmartStudyFunc.Models;
using SmartStudyFunc.Utils;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Interface for retrieving syllabus content via RAG for answer evaluation
    /// </summary>
    public interface ISyllabusRagService
    {
        /// <summary>
        /// Retrieves the most relevant syllabus chunks for a given question.
        /// Uses embedding similarity search filtered by class/subject/chapter.
        /// </summary>
        Task<List<SyllabusChunk>> GetRelevantSyllabusChunksAsync(
            string questionText,
            string className,
            string subject,
            string chapter,
            int topN = 5,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Service for retrieving syllabus content using embeddings and similarity search.
    /// Used by WrittenAnswerEvaluationService to generate expected answers from syllabus.
    /// </summary>
    public class SyllabusRagService : ISyllabusRagService
    {
        private readonly string _connectionString;
        private readonly EmbeddingService _embeddingService;
        private readonly ILogger<SyllabusRagService> _logger;

        private const int DefaultCommandTimeout = 120;

        public SyllabusRagService(
            IConfiguration configuration,
            EmbeddingService embeddingService,
            ILogger<SyllabusRagService> logger)
        {
            var connString = configuration.GetConnectionString("SqlDb")
                ?? configuration["ConnectionStrings:SqlDb"]
                ?? throw new InvalidOperationException("Missing connection string: SqlDb");

            var builder = new SqlConnectionStringBuilder(connString)
            {
                ConnectTimeout = 30,
                CommandTimeout = DefaultCommandTimeout,
                ConnectRetryCount = 3,
                Pooling = true
            };
            _connectionString = builder.ConnectionString;

            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves the most relevant syllabus chunks for a given question.
        /// 
        /// FLOW:
        /// 1. Generate embedding for the question text
        /// 2. Query all chunks filtered by class/subject/chapter
        /// 3. Compute cosine similarity in memory
        /// 4. Return top N most relevant chunks
        /// </summary>
        public async Task<List<SyllabusChunk>> GetRelevantSyllabusChunksAsync(
            string questionText,
            string className,
            string subject,
            string chapter,
            int topN = 5,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(questionText))
            {
                _logger.LogWarning("Empty question text provided for syllabus RAG");
                return new List<SyllabusChunk>();
            }

            try
            {
                // Step 1: Generate embedding for the question
                _logger.LogDebug(
                    "Generating embedding for question: {QuestionPreview}...",
                    questionText.Substring(0, Math.Min(50, questionText.Length)));

                byte[] questionEmbedding;
                try
                {
                    questionEmbedding = await _embeddingService.CreateEmbedding(questionText);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create embedding for question");
                    return new List<SyllabusChunk>();
                }

                // Step 2: Query chunks filtered by class/subject/chapter
                var chunks = await GetChunksWithEmbeddingsAsync(
                    className, subject, chapter, cancellationToken);

                if (chunks.Count == 0)
                {
                    _logger.LogWarning(
                        "No syllabus chunks found for Class={ClassName}, Subject={Subject}, Chapter={Chapter}",
                        className, subject, chapter);
                    
                    // Fallback: try without chapter filter
                    chunks = await GetChunksWithEmbeddingsAsync(
                        className, subject, null, cancellationToken);
                    
                    if (chunks.Count == 0)
                    {
                        _logger.LogWarning(
                            "No syllabus chunks found even without chapter filter");
                        return new List<SyllabusChunk>();
                    }
                }

                _logger.LogDebug("Found {Count} syllabus chunks, computing similarity", chunks.Count);

                // Step 3: Compute similarity for each chunk
                var queryVector = EmbeddingMath.BytesToFloatArray(questionEmbedding);
                var scoredChunks = new List<SyllabusChunk>();

                foreach (var chunk in chunks)
                {
                    if (chunk.Embedding == null || chunk.Embedding.Length == 0)
                        continue;

                    try
                    {
                        var chunkVector = EmbeddingMath.BytesToFloatArray(chunk.Embedding);
                        var similarity = EmbeddingMath.CosineSimilarity(queryVector, chunkVector);

                        scoredChunks.Add(new SyllabusChunk
                        {
                            ChunkId = chunk.ChunkId,
                            ChunkText = chunk.ChunkText,
                            TopicTitle = chunk.TopicTitle,
                            Similarity = similarity
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to compute similarity for chunk {ChunkId}", chunk.ChunkId);
                    }
                }

                // Step 4: Sort by similarity and return top N
                var result = scoredChunks
                    .OrderByDescending(c => c.Similarity)
                    .Take(topN)
                    .ToList();

                var topSimilarity = result.FirstOrDefault()?.Similarity ?? 0;
                
                // Warn if similarity is low - may indicate syllabus mismatch
                if (topSimilarity < 0.5 && result.Count > 0)
                {
                    _logger.LogWarning(
                        "Low syllabus similarity ({TopSim:F4}) for Class={ClassName}, Subject={Subject}, Chapter={Chapter}. " +
                        "Expected answer quality may be affected.",
                        topSimilarity, className, subject, chapter);
                }

                _logger.LogInformation(
                    "Retrieved {Count} relevant syllabus chunks for Class={ClassName}, Subject={Subject}. " +
                    "Top similarity: {TopSim:F4}",
                    result.Count, className, subject, topSimilarity);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to retrieve syllabus chunks for Class={ClassName}, Subject={Subject}, Chapter={Chapter}",
                    className, subject, chapter);
                return new List<SyllabusChunk>();
            }
        }

        /// <summary>
        /// Retrieves chunks with embeddings filtered by class/subject/chapter
        /// </summary>
        private async Task<List<ChunkWithEmbedding>> GetChunksWithEmbeddingsAsync(
            string className,
            string subject,
            string? chapter,
            CancellationToken cancellationToken)
        {
            // Build dynamic query based on filters
            var sql = @"
                SELECT 
                    fc.Id AS ChunkId,
                    fc.ChunkText,
                    fc.TopicTitle,
                    ce.EmbeddingVector AS Embedding
                FROM FileChunks fc
                INNER JOIN ChunkEmbeddings ce ON fc.Id = ce.ChunkId
                INNER JOIN UploadedFiles uf ON fc.UploadedFileId = uf.Id
                WHERE ce.EmbeddingVector IS NOT NULL
                  AND uf.ClassName = @ClassName
                  AND uf.Subject = @Subject";

            if (!string.IsNullOrWhiteSpace(chapter))
            {
                sql += " AND uf.Chapter = @Chapter";
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                var chunks = await conn.QueryAsync<ChunkWithEmbedding>(
                    sql,
                    new { ClassName = className, Subject = subject, Chapter = chapter },
                    commandTimeout: DefaultCommandTimeout);

                return chunks.ToList();
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex,
                    "SQL error retrieving chunks for Class={ClassName}, Subject={Subject}",
                    className, subject);
                return new List<ChunkWithEmbedding>();
            }
        }

        /// <summary>
        /// Internal DTO for chunk with embedding bytes
        /// </summary>
        private class ChunkWithEmbedding
        {
            public int ChunkId { get; set; }
            public string ChunkText { get; set; } = string.Empty;
            public string TopicTitle { get; set; } = string.Empty;
            public byte[]? Embedding { get; set; }
        }
    }
}
