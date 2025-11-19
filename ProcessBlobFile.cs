using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using SmartStudyFunc.Services;
using SmartStudyFunc.Helpers;
using SmartStudyFunc.Utils;
using System;
using System.IO;
using System.Threading.Tasks;


namespace SmartStudyFunc
{
    /// <summary>
    /// Azure Function that processes PDF files uploaded to blob storage.
    /// Extracts text, chunks it, creates embeddings, and stores in SQL database.
    /// </summary>
    public class ProcessBlobFile
    {
        private readonly ILogger<ProcessBlobFile> _logger;
        private readonly IConfiguration _configuration;
        private readonly SqlDb _db;
        private readonly EmbeddingService _embeddingService;

        public ProcessBlobFile(
            ILogger<ProcessBlobFile> logger, 
            IConfiguration configuration,
            EmbeddingService embeddingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));

            var connectionString = _configuration["ConnectionStrings:SqlDb"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("SqlDb connection string is not configured");
            }

            _db = new SqlDb(connectionString);
        }

        [Function(nameof(ProcessBlobFile))]
        public async Task Run(
            [BlobTrigger("textbooks/{className}/{subject}/{chapter}/{name}", Connection = "AzureWebJobsStorage")] Stream blobStream,
            string className,
            string subject,
            string chapter,
            string name)
        {
            try
            {
                _logger.LogInformation("========================================");
                _logger.LogInformation("NEW FILE UPLOADED TO BLOB STORAGE");
                _logger.LogInformation("File: {FileName}", name);
                _logger.LogInformation("Path: textbooks/{ClassName}/{Subject}/{Chapter}/{Name}", className, subject, chapter, name);
                _logger.LogInformation("Timestamp: {Timestamp}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"));
                _logger.LogInformation("========================================");

                // 1. Read blob stream to byte array
                byte[] fileBytes = await ReadBlobAsync(blobStream);
                
                // 2. Extract metadata
                var fileExtension = Path.GetExtension(name).ToLowerInvariant();
                var fileSize = fileBytes.Length;

                _logger.LogInformation(
                    "File details: {FileName} | Size: {Size:N0} bytes | Ext: {Extension}",
                    name, fileSize, fileExtension);

                // 3. Insert file metadata to database
                int fileId = await _db.InsertUploadedFile(name, fileSize, fileExtension, className, subject, chapter);
                _logger.LogInformation("Inserted file metadata, ID={FileId}", fileId);

                // 4. Extract text from PDF
                string extractedText = ExtractText(fileBytes, fileExtension, name);
                _logger.LogInformation("Extracted {Length} characters from PDF", extractedText.Length);

                // 5. Create semantic chunks
                var chunks = Chunker.CreateSemanticChunks(extractedText);
                _logger.LogInformation("Total semantic chunks: {Count}", chunks.Count);

                // 6. Process each chunk: insert chunk + create embedding + insert embedding
                int processedCount = await ProcessChunksAsync(chunks, fileId);

                _logger.LogInformation("========================================");
                _logger.LogInformation("PROCESSING COMPLETE");
                _logger.LogInformation("File: {Name}", name);
                _logger.LogInformation("File ID: {FileId}", fileId);
                _logger.LogInformation("Total Chunks Created: {Count}", processedCount);
                _logger.LogInformation("Status: SUCCESS");
                _logger.LogInformation("========================================");
            }
            catch (Exception ex)
            {
                _logger.LogError("========================================");
                _logger.LogError("PROCESSING FAILED");
                _logger.LogError(ex, "Error processing file: {FileName}", name);
                _logger.LogError("========================================");
                throw;
            }
        }

        private static async Task<byte[]> ReadBlobAsync(Stream blobStream)
        {
            using var memoryStream = new MemoryStream();
            await blobStream.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }

        private string ExtractText(byte[] fileBytes, string fileExtension, string fileName)
        {
            if (fileExtension == ".pdf")
            {
                try
                {
                    return PdfTextExtractorHelper.Extract(fileBytes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PDF extraction failed: {FileName}", fileName);
                    throw;
                }
            }

            if (fileExtension is ".jpg" or ".jpeg" or ".png" or ".bmp")
            {
                return "[Image OCR pending - not yet implemented]";
            }

            throw new NotSupportedException($"File format not supported: {fileExtension}");
        }

        private async Task<int> ProcessChunksAsync(System.Collections.Generic.List<string> chunks, int fileId)
        {
            int processedCount = 0;

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];

                // Generate metadata for chunk
                string topicTitle = GenerateTopicTitle(chunk, i);
                string summary = GenerateSummary(chunk);
                int tokenCount = EstimateTokenCount(chunk);

                // Insert chunk to database
                int chunkId = await _db.InsertChunk(
                    uploadedFileId: fileId,
                    topicTitle: topicTitle,
                    summary: summary,
                    chunkText: chunk,
                    tokenCount: tokenCount,
                    pageFrom: 0,  // TODO: Extract page numbers from PDF
                    pageTo: 0,
                    chunkType: "text"
                );

                _logger.LogInformation(
                    "Inserted chunk {Index}/{Total} -> ChunkId={ChunkId}",
                    i + 1, chunks.Count, chunkId);

                // Create embedding and insert to database
                byte[] embedding = await _embeddingService.CreateEmbedding(chunk);
                await _db.InsertEmbedding(chunkId, embedding);

                processedCount++;
            }

            return processedCount;
        }

        private static int EstimateTokenCount(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);
        }

        private static string GenerateTopicTitle(string chunk, int index)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                return $"Chunk {index + 1}";
            }

            // Try to get first sentence
            int sentenceEnd = chunk.IndexOf('.');
            if (sentenceEnd > 0 && sentenceEnd < 100)
            {
                return chunk[..sentenceEnd].Trim();
            }

            // Otherwise get first 100 characters
            int maxLength = Math.Min(100, chunk.Length);
            string title = chunk[..maxLength].Trim();

            return title.Length < chunk.Length ? title + "..." : title;
        }

        private static string GenerateSummary(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                return string.Empty;
            }

            int maxLength = Math.Min(250, chunk.Length);
            string summary = chunk[..maxLength].Trim();

            return summary.Length < chunk.Length ? summary + "..." : summary;
        }
    }
}
