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
    /// Study Notes Generator
    /// Creates structured study notes from uploaded documents
    /// </summary>
    public class GenerateStudyNotes
    {
        private readonly ILogger<GenerateStudyNotes> _logger;
        private readonly IConfiguration _configuration;
        private readonly SqlDb _db;
        private readonly EmbeddingService _embeddingService;
        private readonly OpenAiService _openAiService;

        public GenerateStudyNotes(
            ILogger<GenerateStudyNotes> logger,
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
        /// HTTP POST endpoint for generating study notes
        /// Route: /study/notes
        /// </summary>
        [Function("GenerateStudyNotes")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "study/notes")] HttpRequestData req)
        {
            _logger.LogInformation("Generate Study Notes request received");

            try
            {
                // STEP A — Validate input
                StudyNotesRequest? notesRequest = null;
                try
                {
                    var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    notesRequest = JsonSerializer.Deserialize<StudyNotesRequest>(requestBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Invalid JSON in request body: {Message}", ex.Message);
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Invalid JSON format");
                }

                if (notesRequest == null || string.IsNullOrWhiteSpace(notesRequest.Topic))
                {
                    _logger.LogWarning("Empty topic received");
                    return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "Topic is required");
                }

                var topic = notesRequest.Topic.Trim();
                var format = notesRequest.Format ?? "bullet-points"; // Default format
                _logger.LogInformation("Generating study notes for topic: {Topic}, format: {Format}", topic, format);

                // STEP B — Convert topic to embedding to find relevant content
                byte[] topicEmbedding;
                try
                {
                    topicEmbedding = await _embeddingService.CreateEmbedding(topic);
                    _logger.LogInformation("Created embedding for topic ({Size} bytes)", topicEmbedding.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create embedding for topic");
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        "Failed to create embedding for topic");
                }

                // STEP C — Retrieve relevant chunks (more chunks for comprehensive notes)
                List<SearchResultChunk> relevantChunks;
                try
                {
                    relevantChunks = await GetTopMatchingChunks(topicEmbedding, topN: 15); // More chunks for comprehensive notes
                    _logger.LogInformation("Retrieved {Count} matching chunks", relevantChunks.Count);

                    if (relevantChunks.Count == 0)
                    {
                        _logger.LogWarning("No chunks found for topic");
                        return await CreateErrorResponse(req, HttpStatusCode.NotFound, 
                            "No relevant content found for this topic. Please upload documents first.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve matching chunks");
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        "Failed to retrieve matching chunks");
                }

                // STEP D — Build specialized prompt for study notes generation
                var contextText = string.Join("\n\n---\n\n", relevantChunks.Select(c => c.ChunkText));
                
                var formatInstructions = GetFormatInstructions(format);
                
                var promptBuilder = new System.Text.StringBuilder();
                promptBuilder.AppendLine("### ROLE: You are Smarty, an expert study notes creator.");
                promptBuilder.AppendLine("### TASK: Create comprehensive, well-organized study notes on the given topic.");
                promptBuilder.AppendLine($"### FORMAT: {formatInstructions}");
                promptBuilder.AppendLine("### GUIDELINES:");
                promptBuilder.AppendLine("- Include key concepts, definitions, and important details");
                promptBuilder.AppendLine("- Organize information logically");
                promptBuilder.AppendLine("- Highlight essential points that students should remember");
                promptBuilder.AppendLine("- Use clear, concise language");
                promptBuilder.AppendLine("- Add relevant examples where helpful");
                promptBuilder.AppendLine("\n### SOURCE CONTENT:");
                promptBuilder.AppendLine(contextText);
                promptBuilder.AppendLine($"\n### TOPIC: {topic}");
                promptBuilder.AppendLine("\n### STUDY NOTES:");
                
                var prompt = promptBuilder.ToString();

                _logger.LogInformation("Built prompt for study notes generation: {Length} chars", prompt.Length);

                // STEP E — Generate study notes using GPT-4o-mini
                string studyNotes;
                try
                {
                    studyNotes = await _openAiService.GetChatCompletionAsync(prompt);
                    _logger.LogInformation("Generated study notes: {Length} chars", studyNotes.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate study notes: {Message}", ex.Message);
                    return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                        $"Failed to generate study notes: {ex.Message}");
                }

                // STEP F — Return response
                var response = new StudyNotesResponse
                {
                    Topic = topic,
                    Format = format,
                    Notes = studyNotes,
                    ChunksUsed = relevantChunks.Select(c => c.ChunkId).ToList(),
                    ChunkCount = relevantChunks.Count
                };

                return await CreateSuccessResponse(req, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in study notes generation: {Message}", ex.Message);
                return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, 
                    "An unexpected error occurred");
            }
        }

        private string GetFormatInstructions(string format)
        {
            return format.ToLower() switch
            {
                "bullet-points" => "Use bullet points (•) to organize key information. Group related points under clear headings.",
                "outline" => "Create a hierarchical outline with numbered sections (1, 1.1, 1.2, etc.) and subsections.",
                "flashcards" => "Format as Q&A pairs suitable for flashcards. Use 'Q:' for questions and 'A:' for answers.",
                "summary" => "Write a concise narrative summary covering the main points in paragraph form.",
                _ => "Use bullet points (•) to organize key information. Group related points under clear headings."
            };
        }

        /// <summary>
        /// Retrieves top N matching chunks by computing cosine similarity
        /// </summary>
        private async Task<List<SearchResultChunk>> GetTopMatchingChunks(byte[] queryEmbedding, int topN)
        {
            var allChunks = await _db.GetAllChunksWithEmbeddings();

            if (allChunks.Count == 0)
            {
                _logger.LogWarning("No chunks with embeddings found in database");
                return new List<SearchResultChunk>();
            }

            _logger.LogInformation("Computing similarity for {Count} chunks", allChunks.Count);

            // Convert query embedding to float array once
            var queryVector = EmbeddingMath.BytesToFloatArray(queryEmbedding);

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

            // Return top N chunks sorted by similarity
            var topChunks = allChunks
                .OrderByDescending(c => c.Similarity)
                .Take(topN)
                .ToList();

            _logger.LogInformation("Selected top {TopN} chunks out of {Total}", topChunks.Count, allChunks.Count);
            return topChunks;
        }

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

        private async Task<HttpResponseData> CreateErrorResponse(
            HttpRequestData req, 
            HttpStatusCode statusCode, 
            string message)
        {
            var response = req.CreateResponse(statusCode);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            var errorObj = new { error = message };
            var json = JsonSerializer.Serialize(errorObj, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            await response.WriteStringAsync(json);
            return response;
        }
    }
}
