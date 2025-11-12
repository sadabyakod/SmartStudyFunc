using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services;
using SmartStudyFunc.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// RAG (Retrieval-Augmented Generation) Search API
    /// Accepts a question, retrieves relevant chunks, and generates an answer using GPT-4o-mini
    /// </summary>
    public class SearchRagQuery
    {
        private readonly ILogger<SearchRagQuery> _logger;
        private readonly IConfiguration _configuration;
        private readonly SqlDb _db;
        private readonly EmbeddingService _embeddingService;
        private readonly OpenAiService _openAiService;

        public SearchRagQuery(
            ILogger<SearchRagQuery> logger,
            IConfiguration configuration,
            EmbeddingService embeddingService,
            OpenAiService openAiService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
            _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));

            var connString = _configuration.GetConnectionString("SqlDb")
                ?? throw new InvalidOperationException("Missing connection string: SqlDb");
            _db = new SqlDb(connString);
        }

        /// <summary>
        /// HTTP POST endpoint for RAG search
        /// Route: /rag/search
        /// </summary>
        [Function("SearchRagQuery")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "rag/search")] HttpRequestData req)
        {
            _logger.LogInformation("RAG Search request received");

            try
            {
                // STEP A — Validate input
                SearchRequest? searchRequest = null;
                try
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    searchRequest = JsonSerializer.Deserialize<SearchRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Invalid JSON in request body: {Message}", ex.Message);
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid JSON format");
                }

                if (searchRequest == null || string.IsNullOrWhiteSpace(searchRequest.Question))
                {
                    _logger.LogWarning("Empty question received");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Question is required");
                }

                var question = searchRequest.Question.Trim();
                _logger.LogInformation("Processing question: {Question}", question);

                // STEP B — Convert question to embedding
                byte[] questionEmbedding;
                try
                {
                    questionEmbedding = await _embeddingService.CreateEmbedding(question);
                    _logger.LogInformation("Created embedding for question ({Size} bytes)", questionEmbedding.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create embedding for question");
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        "Failed to create embedding for question");
                }

                // STEP C — Query SQL Server and compute similarity in C#
                List<SearchResultChunk> topChunks;
                try
                {
                    topChunks = await GetTopMatchingChunks(questionEmbedding, topN: 5);
                    _logger.LogInformation("Retrieved {Count} matching chunks", topChunks.Count);

                    if (topChunks.Count == 0)
                    {
                        _logger.LogWarning("No chunks found in database");
                        return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                            "No relevant content found. Please upload documents first.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve matching chunks");
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        "Failed to retrieve matching chunks");
                }

                // STEP D — Build prompt for GPT-4o-mini
                var contextText = string.Join("\n\n---\n\n", topChunks.Select(c => c.ChunkText));
                var prompt = $@"Use ONLY the context below to answer briefly and accurately.

Context:
{contextText}

Question:
{question}

Answer:";

                _logger.LogInformation("Built prompt with {ChunkCount} chunks, total length: {Length} chars", 
                    topChunks.Count, contextText.Length);

                // STEP E — Call Azure OpenAI GPT-4o-mini chat completion
                string answer;
                try
                {
                    answer = await _openAiService.GetChatCompletionAsync(prompt);
                    _logger.LogInformation("Received answer from GPT-4o-mini: {AnswerLength} chars", answer.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get chat completion from OpenAI");
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        "Failed to generate answer");
                }

                // STEP F — Log results into RAGSearchLogs
                var chunkIds = topChunks.Select(c => c.ChunkId).ToList();
                var chunkIdsString = string.Join(",", chunkIds);
                var confidence = topChunks.Any() ? topChunks.Max(c => c.Similarity) : 0.0;

                try
                {
                    await _db.InsertRagSearchLog(
                        question: question,
                        answer: answer,
                        retrievedChunkIds: chunkIdsString,
                        confidenceScore: confidence
                    );
                    _logger.LogInformation("Logged search to RAGSearchLogs table");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to log search results (non-critical): {Message}", ex.Message);
                    // Continue anyway - logging failure shouldn't break the response
                }

                // STEP G — Return JSON response
                var response = new SearchResponse
                {
                    Answer = answer,
                    ChunksUsed = chunkIds,
                    Confidence = Math.Round(confidence, 4)
                };

                return await CreateSuccessResponse(req, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in RAG search: {Message}", ex.Message);
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An unexpected error occurred");
            }
        }

        /// <summary>
        /// Retrieves top N matching chunks by computing cosine similarity in C#
        /// This is the fallback approach when SQL vector operations are complex
        /// </summary>
        private async Task<List<SearchResultChunk>> GetTopMatchingChunks(byte[] questionEmbedding, int topN)
        {
            // Fetch all chunks with embeddings from database
            var allChunks = await _db.GetAllChunksWithEmbeddings();

            if (allChunks.Count == 0)
            {
                _logger.LogWarning("No chunks with embeddings found in database");
                return new List<SearchResultChunk>();
            }

            _logger.LogInformation("Computing similarity for {Count} chunks", allChunks.Count);

            // Convert query embedding to float array once
            var queryVector = EmbeddingMath.BytesToFloatArray(questionEmbedding);

            // Compute cosine similarity for each chunk
            foreach (var chunk in allChunks)
            {
                try
                {
                    var chunkVector = EmbeddingMath.BytesToFloatArray(chunk.Embedding);
                    chunk.Similarity = EmbeddingMath.CosineSimilarity(queryVector, chunkVector);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to compute similarity for chunk {ChunkId}, skipping", chunk.ChunkId);
                    chunk.Similarity = 0.0;
                }
            }

            // Sort by similarity descending and take top N
            var topChunks = allChunks
                .OrderByDescending(c => c.Similarity)
                .Take(topN)
                .ToList();

            _logger.LogInformation("Top {TopN} chunks selected. Best similarity: {Best:F4}, Worst: {Worst:F4}",
                topN,
                topChunks.Any() ? topChunks.First().Similarity : 0,
                topChunks.Any() ? topChunks.Last().Similarity : 0);

            return topChunks;
        }

        /// <summary>
        /// Creates a successful JSON response
        /// </summary>
        private async Task<HttpResponseData> CreateSuccessResponse(HttpRequestData req, SearchResponse data)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            
            await response.WriteStringAsync(json);
            return response;
        }

        /// <summary>
        /// Creates an error JSON response
        /// </summary>
        private async Task<HttpResponseData> CreateErrorResponse(
            HttpRequestData req, 
            HttpStatusCode statusCode, 
            string message)
        {
            var response = req.CreateResponse(statusCode);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var errorData = new { error = message };
            var json = JsonSerializer.Serialize(errorData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            await response.WriteStringAsync(json);
            return response;
        }
    }
}
