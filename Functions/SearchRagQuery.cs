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
                SearchRequestWithHistory? searchRequest = null;
                try
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    searchRequest = JsonSerializer.Deserialize<SearchRequestWithHistory>(requestBody, new JsonSerializerOptions
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
                var conversationId = searchRequest.ConversationId ?? Guid.NewGuid();
                _logger.LogInformation("Processing question: {Question} (ConversationId: {ConversationId})", 
                    question, conversationId);

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

                // STEP D — Retrieve conversation history (if exists)
                List<ChatMessage> chatHistory = new List<ChatMessage>();
                if (searchRequest.ConversationId.HasValue)
                {
                    try
                    {
                        chatHistory = await _db.GetConversationHistory(searchRequest.ConversationId.Value, maxMessages: 10);
                        _logger.LogInformation("Retrieved {Count} previous messages for conversation {ConversationId}", 
                            chatHistory.Count, conversationId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to retrieve chat history (continuing without it): {Message}", ex.Message);
                        // Continue without chat history - not critical
                    }
                }

                // Save user's question to chat history
                try
                {
                    await _db.InsertChatMessage(
                        conversationId: conversationId,
                        role: "user",
                        message: question
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save user message to chat history: {Message}", ex.Message);
                    // Non-critical - continue
                }

                // STEP E — Build prompt for GPT-4o-mini with engaging, friendly tone
                var contextText = string.Join("\n\n---\n\n", topChunks.Select(c => c.ChunkText));
                
                var promptBuilder = new System.Text.StringBuilder();
                promptBuilder.AppendLine("### ROLE: You are Smarty, a friendly and intelligent AI study assistant.");
                promptBuilder.AppendLine("### TONE: Encouraging and conversational, like a kind teacher.");
                promptBuilder.AppendLine("### TASK:");
                promptBuilder.AppendLine("- Answer clearly and simply.");
                promptBuilder.AppendLine("- End with a short follow-up question (e.g., 'Would you like an example?' or 'Should I explain more?').");
                promptBuilder.AppendLine("- Use one emoji if it fits naturally.");
                
                // Include conversation history if available
                if (chatHistory.Any())
                {
                    promptBuilder.AppendLine("\n### CONVERSATION HISTORY:");
                    foreach (var msg in chatHistory)
                    {
                        promptBuilder.AppendLine($"{msg.Role.ToUpper()}: {msg.Message}");
                    }
                }
                
                promptBuilder.AppendLine("\n### CONTEXT (syllabus):");
                promptBuilder.AppendLine(contextText);
                promptBuilder.AppendLine("\n### STUDENT QUESTION:");
                promptBuilder.AppendLine(question);
                promptBuilder.AppendLine("\n### YOUR RESPONSE:");
                
                var prompt = promptBuilder.ToString();

                _logger.LogInformation("Built prompt with {ChunkCount} chunks, {HistoryCount} history messages, total length: {Length} chars", 
                    topChunks.Count, chatHistory.Count, contextText.Length);

                // STEP E — Call Azure OpenAI GPT-4o-mini chat completion
                string answer;
                try
                {
                    answer = await _openAiService.GetChatCompletionAsync(prompt);
                    _logger.LogInformation("Received answer from GPT-4o-mini: {AnswerLength} chars", answer.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get chat completion from OpenAI. Error: {ErrorType} - {ErrorMessage}. InnerException: {InnerException}", 
                        ex.GetType().Name, ex.Message, ex.InnerException?.Message ?? "None");
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        $"Failed to generate answer: {ex.Message}");
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

                // STEP G — Save AI's response to chat history
                try
                {
                    await _db.InsertChatMessage(
                        conversationId: conversationId,
                        role: "assistant",
                        message: answer,
                        chunksUsed: chunkIdsString,
                        confidence: confidence
                    );
                    _logger.LogInformation("Saved assistant response to chat history");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save assistant message to chat history: {Message}", ex.Message);
                    // Non-critical - continue
                }

                // STEP H — Return JSON response with ConversationId
                var response = new SearchResponseWithHistory
                {
                    Answer = answer,
                    ChunksUsed = chunkIds,
                    Confidence = Math.Round(confidence, 4),
                    ConversationId = conversationId
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
        private async Task<HttpResponseData> CreateSuccessResponse(HttpRequestData req, object data)
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
