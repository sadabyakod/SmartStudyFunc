using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Text;

namespace SmartStudyFunc
{
    public class ProcessBlobFile
    {
        private readonly ILogger<ProcessBlobFile> _logger;
        private readonly IConfiguration _configuration;
        private readonly SqlDb _db;

        public ProcessBlobFile(ILogger<ProcessBlobFile> logger, IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            var connectionString = _configuration["ConnectionStrings:SqlDb"];
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("SqlDb connection string is not configured");
            }

            _db = new SqlDb(connectionString);
        }

        [Function(nameof(ProcessBlobFile))]
        public async Task Run(
            [BlobTrigger("textbooks/{name}", Connection = "AzureWebJobsStorage")] Stream blobStream,
            string name)
        {
            try
            {
                _logger.LogInformation("Processing new file: {FileName}", name);

                byte[] fileBytes;
                using (var ms = new MemoryStream())
                {
                    await blobStream.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                }

                long fileSize = fileBytes.Length;
                string extension = Path.GetExtension(name)?.ToLowerInvariant() ?? "";

                _logger.LogInformation("File details: {FileName} | Size: {Bytes:N0} bytes | Ext: {Extension}",
                    name, fileSize, extension);

                int fileId = await _db.InsertUploadedFile(name, fileSize, extension);
                _logger.LogInformation("Inserted file metadata, ID={FileId}", fileId);

                string extractedText = ExtractTextBasedOnType(fileBytes, extension, name);

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    _logger.LogWarning("No extractable text in file: {FileName}", name);
                    return;
                }

                List<string> chunks = Chunker.CreateSemanticChunks(extractedText);
                _logger.LogInformation("Total semantic chunks: {Count}", chunks.Count);

                int processedCount = 0;

                for (int i = 0; i < chunks.Count; i++)
                {
                    try
                    {
                        string chunk = chunks[i];
                        int tokenCount = EstimateTokenCount(chunk);
                        string topicTitle = GenerateTopicTitle(chunk, i);
                        string summary = GenerateSummary(chunk);

                        int chunkId = await _db.InsertChunk(
                            uploadedFileId: fileId,
                            topicTitle: topicTitle,
                            summary: summary,
                            chunkText: chunk,
                            tokenCount: tokenCount,
                            pageFrom: 0,
                            pageTo: 0,
                            chunkType: "text"
                        );

                        _logger.LogInformation(
                            "Inserted chunk {Index}/{Total} -> ChunkId={ChunkId}",
                            i + 1, chunks.Count, chunkId);

                        byte[] embedding = await EmbeddingService.CreateFakeEmbedding(chunk);
                        await _db.InsertEmbedding(chunkId, embedding);

                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing chunk {Index}", i + 1);
                    }
                }

                _logger.LogInformation(
                    "Processing complete: File={Name}, FileId={FileId}, Chunks={Count}",
                    name, fileId, processedCount
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CRITICAL ERROR processing file {FileName}", name);
                throw;
            }
        }

        private string ExtractTextBasedOnType(byte[] fileBytes, string ext, string name)
        {
            if (ext == ".pdf")
            {
                try
                {
                    return PdfTextExtractorHelper.ExtractText(fileBytes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PDF extraction failed: {FileName}", name);
                    throw;
                }
            }

            if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp")
            {
                return "[Image OCR pending]";
            }

            return "[Unsupported file format]";
        }

        private int EstimateTokenCount(string text) =>
            string.IsNullOrWhiteSpace(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

        private string GenerateTopicTitle(string chunk, int index)
        {
            if (string.IsNullOrWhiteSpace(chunk)) return $"Chunk {index + 1}";

            int end = chunk.IndexOf('.');
            if (end > 0 && end < 100)
                return chunk[..end].Trim();

            int maxLen = Math.Min(100, chunk.Length);
            string title = chunk[..maxLen].Trim();

            return title.Length < chunk.Length ? title + "..." : title;
        }

        private string GenerateSummary(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk)) return "";

            int max = Math.Min(250, chunk.Length);
            string summary = chunk[..max].Trim();

            return summary.Length < chunk.Length ? summary + "..." : summary;
        }
    }

    // ---------------------------------------------------
    // PDF TEXT EXTRACTION (FULLY IMPLEMENTED)
    // ---------------------------------------------------
    public static class PdfTextExtractorHelper
    {
        public static string ExtractText(byte[] pdfBytes)
        {
            using var pdf = PdfDocument.Open(pdfBytes);
            var sb = new StringBuilder();

            foreach (var page in pdf.GetPages())
            {
                sb.AppendLine(page.Text);
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    // ---------------------------------------------------
    // TEXT CHUNKING
    // ---------------------------------------------------
    public static class Chunker
    {
        public static List<string> CreateSemanticChunks(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var chunks = new List<string>();
            const int limit = 1000;

            var paragraphs = text.Split(
                new[] { "\n\n", "\r\n\r\n" },
                StringSplitOptions.RemoveEmptyEntries);

            string current = "";

            foreach (string p in paragraphs)
            {
                if (current.Length + p.Length > limit)
                {
                    chunks.Add(current.Trim());
                    current = p;
                }
                else
                {
                    current += (current == "" ? "" : "\n\n") + p;
                }
            }

            if (!string.IsNullOrWhiteSpace(current))
                chunks.Add(current.Trim());

            if (chunks.Count == 0)
            {
                for (int i = 0; i < text.Length; i += limit)
                {
                    chunks.Add(text.Substring(i, Math.Min(limit, text.Length - i)));
                }
            }

            return chunks;
        }
    }

    // ---------------------------------------------------
    // EMBEDDING GENERATOR (FAKE FOR NOW)
    // ---------------------------------------------------
    public static class EmbeddingService
    {
        public static async Task<byte[]> CreateFakeEmbedding(string text)
        {
            await Task.Delay(5);

            var random = new Random(text.GetHashCode());
            var vec = new float[1536];

            for (int i = 0; i < vec.Length; i++)
                vec[i] = (float)(random.NextDouble() * 2 - 1);

            var bytes = new byte[vec.Length * sizeof(float)];
            Buffer.BlockCopy(vec, 0, bytes, 0, bytes.Length);

            return bytes;
        }
    }
}
