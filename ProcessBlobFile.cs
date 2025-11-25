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
    /// For textbooks: Extracts text, chunks, embeddings (existing pipeline).
    /// For syllabus: ONLY extracts text and triggers ExtractChapters (NEW).
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
            _logger = logger;
            _configuration = configuration;
            _embeddingService = embeddingService;

            var connectionString = _configuration["ConnectionStrings:SqlDb"];
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("SqlDb connection string is not configured");

            _db = new SqlDb(connectionString);
        }

        // UPDATED: Now triggers for BOTH textbooks and syllabus folders
        [Function(nameof(ProcessBlobFile))]
        public async Task Run(
            [BlobTrigger("textbooks/{className}/{subject}/{chapter}/{name}", Connection = "AzureWebJobsStorage")] Stream blobStream,
            string className,
            string subject,
            string chapter,
            string name,
            CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;
            byte[]? textbookBytes = null;
            
            try
            {
                // Check for cancellation at start
                cancellationToken.ThrowIfCancellationRequested();
                
                _logger.LogInformation("========================================");
                _logger.LogInformation("NEW FILE UPLOADED TO BLOB");
                _logger.LogInformation("Class: {ClassName}, Subject: {Subject}, Chapter: {Chapter}", className, subject, chapter);
                _logger.LogInformation("File: {File}", name);
                _logger.LogInformation("========================================");

                // Validate inputs
                if (string.IsNullOrWhiteSpace(name))
                {
                    _logger.LogError("Blob name is null or empty. Aborting processing.");
                    return;
                }

                if (blobStream == null || !blobStream.CanRead)
                {
                    _logger.LogError("Blob stream is null or cannot be read for file: {Name}", name);
                    return;
                }

                // TEXTBOOK PROCESSING with comprehensive error handling
                try
                {
                    // Check stream size before reading to prevent OOM
                    if (blobStream.CanSeek && blobStream.Length > 100 * 1024 * 1024) // 100MB limit
                    {
                        _logger.LogError("Blob file too large: {Size} bytes. Maximum: 100MB. File: {Name}", blobStream.Length, name);
                        return;
                    }

                    textbookBytes = await ReadBlobAsync(blobStream, cancellationToken);
                    _logger.LogInformation("Successfully read blob stream: {Size} bytes", textbookBytes.Length);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Processing cancelled for file: {Name}", name);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read blob stream for file: {Name}. Aborting.", name);
                    return; // Don't throw - just log and exit gracefully
                }
                finally
                {
                    // Ensure blob stream is properly disposed
                    if (blobStream != null)
                    {
                        try { await blobStream.DisposeAsync(); } catch { /* Ignore disposal errors */ }
                    }
                }

                if (textbookBytes == null || textbookBytes.Length == 0)
                {
                    _logger.LogWarning("Blob is empty for file: {Name}. Aborting processing.", name);
                    return;
                }

                string fileExtension = Path.GetExtension(name).ToLowerInvariant();
                int fileSize = textbookBytes.Length;

                _logger.LogInformation("Processing textbook: Size={Size}, Ext={Ext}", fileSize, fileExtension);

                // Insert file metadata with retry already built into SqlDb
                int fileId;
                try
                {
                    fileId = await _db.InsertUploadedFile(name, fileSize, fileExtension, className, subject, chapter);
                    _logger.LogInformation("Inserted File Metadata ID={FileId}", fileId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to insert file metadata for: {Name}. Aborting processing.", name);
                    return; // Don't throw - just log and exit gracefully
                }

                // Extract PDF text with robust error handling
                string? extractedText = null;
                try
                {
                    extractedText = ExtractText(textbookBytes, fileExtension, name);
                    _logger.LogInformation("Extracted {Length} chars from PDF", extractedText?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to extract text from PDF: {Name}. File will be marked but not processed.", name);
                    return; // Don't throw - PDF might be corrupted
                }

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    _logger.LogWarning("No text extracted from PDF: {Name}. File appears to be empty or image-only.", name);
                    return;
                }

                // Chunk text with error handling
                System.Collections.Generic.List<string>? chunks = null;
                try
                {
                    chunks = Chunker.CreateSemanticChunks(extractedText);
                    _logger.LogInformation("Chunk count: {Count}", chunks?.Count ?? 0);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to chunk text for: {Name}. Aborting chunk processing.", name);
                    return; // Don't throw - chunking error shouldn't crash function
                }

                if (chunks == null || chunks.Count == 0)
                {
                    _logger.LogWarning("No chunks created for file: {Name}. Text might be too short.", name);
                    return;
                }

                // Process chunks - this is the most critical part that must NEVER crash
                int processedCount = 0;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    processedCount = await ProcessChunksAsync(chunks, fileId, cancellationToken);
                    _logger.LogInformation("Successfully processed {Count}/{Total} chunks", processedCount, chunks.Count);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Chunk processing cancelled for: {Name}. Processed {Count}/{Total} chunks before cancellation.", 
                        name, processedCount, chunks.Count);
                    // Don't throw - partial success is acceptable
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chunk processing failed for: {Name}. Processed {Count}/{Total} chunks before failure.", 
                        name, processedCount, chunks.Count);
                    // Don't throw - partial success is better than complete failure
                }
                finally
                {
                    // Explicitly clear large objects to help GC
                    if (textbookBytes != null && textbookBytes.Length > 10 * 1024 * 1024) // 10MB+
                    {
                        textbookBytes = null;
                        GC.Collect(2, GCCollectionMode.Optimized, false);
                    }
                }

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation("========================================");
                _logger.LogInformation("TEXTBOOK PROCESSING COMPLETE → SUCCESS");
                _logger.LogInformation("File: {Name}", name);
                _logger.LogInformation("Chunks: {Processed}/{Total}", processedCount, chunks.Count);
                _logger.LogInformation("Duration: {Duration:mm\\:ss}", duration);
                _logger.LogInformation("========================================");
            }
            catch (Exception ex)
            {
                // Final catch-all to ensure function NEVER throws
                var duration = DateTime.UtcNow - startTime;
                _logger.LogError(ex, "CRITICAL: Unexpected error in ProcessBlobFile for: {FileName}. Duration: {Duration:mm\\:ss}", name, duration);
                // Do NOT throw - log and exit gracefully
            }
        }

        private static async Task<byte[]> ReadBlobAsync(Stream blobStream, CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            await blobStream.CopyToAsync(ms, 81920, cancellationToken); // 80KB buffer
            return ms.ToArray();
        }

        private string ExtractText(byte[] fileBytes, string fileExtension, string fileName)
        {
            if (fileExtension == ".pdf")
            {
                try
                {
                    // Add timeout protection for PDF extraction
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
                    var extractTask = Task.Run(() => PdfTextExtractorHelper.Extract(fileBytes), cts.Token);
                    
                    var extractedText = extractTask.GetAwaiter().GetResult();
                    
                    if (string.IsNullOrWhiteSpace(extractedText))
                    {
                        _logger.LogWarning("PDF extraction returned empty text for: {FileName}", fileName);
                        return string.Empty;
                    }
                    
                    _logger.LogDebug("Successfully extracted {Length} characters from PDF: {FileName}", 
                        extractedText.Length, fileName);
                    return extractedText;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogError("PDF extraction timeout (3 minutes) for: {FileName}. File may be corrupted or too complex.", fileName);
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PDF extraction failed for: {FileName}. File might be corrupted, password-protected, or malformed.", fileName);
                    return string.Empty; // Don't throw - return empty string
                }
            }

            _logger.LogWarning("Unsupported file extension: {Ext} for file: {FileName}", fileExtension, fileName);
            return string.Empty;
        }

        private async Task<int> ProcessChunksAsync(System.Collections.Generic.List<string> chunks, int fileId, CancellationToken cancellationToken)
        {
            int processedCount = 0;
            int failedCount = 0;
            const int batchSize = 2; // Further reduced from 3 to 2 for maximum stability
            var totalChunks = chunks.Count;

            _logger.LogInformation("Starting chunk processing: {Total} total chunks, batch size: {BatchSize}", totalChunks, batchSize);

            for (int batchStart = 0; batchStart < chunks.Count; batchStart += batchSize)
            {
                // Check for cancellation before each batch
                cancellationToken.ThrowIfCancellationRequested();
                
                int batchEnd = Math.Min(batchStart + batchSize, chunks.Count);
                var batchNumber = (batchStart / batchSize) + 1;
                var totalBatches = (int)Math.Ceiling((double)chunks.Count / batchSize);
                
                _logger.LogInformation("Processing batch {BatchNum}/{TotalBatches}: chunks {Start}-{End} of {Total}", 
                    batchNumber, totalBatches, batchStart + 1, batchEnd, chunks.Count);

                for (int i = batchStart; i < batchEnd; i++)
                {
                    try
                    {
                        var chunk = chunks[i];
                        
                        // Validate chunk before processing
                        if (string.IsNullOrWhiteSpace(chunk))
                        {
                            _logger.LogWarning("Chunk {Index} is empty. Skipping.", i + 1);
                            failedCount++;
                            continue;
                        }

                        string topicTitle = GenerateTopicTitle(chunk, i);
                        string summary = GenerateSummary(chunk);
                        int tokenCount = EstimateTokenCount(chunk);

                        // Insert chunk with built-in retry logic in SqlDb
                        int chunkId;
                        try
                        {
                            chunkId = await _db.InsertChunk(
                                uploadedFileId: fileId,
                                topicTitle: topicTitle,
                                summary: summary,
                                chunkText: chunk,
                                tokenCount: tokenCount,
                                pageFrom: 0,
                                pageTo: 0,
                                chunkType: "text"
                            );
                            _logger.LogInformation("Inserted chunk {Index}/{Total} -> ChunkId={ChunkId}", 
                                i + 1, chunks.Count, chunkId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to insert chunk {Index}/{Total} into database. Skipping chunk.", 
                                i + 1, chunks.Count);
                            failedCount++;
                            continue; // Skip this chunk but continue with others
                        }

                        // Generate and insert embedding with built-in retry logic in EmbeddingService
                        try
                        {
                            // Check cancellation before expensive embedding operation
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            // Add timeout protection for embedding generation (30 seconds per chunk)
                            using var embCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            embCts.CancelAfter(TimeSpan.FromSeconds(30));
                            
                            byte[] emb = await _embeddingService.CreateEmbedding(chunk);
                            
                            if (emb == null || emb.Length == 0)
                            {
                                _logger.LogWarning("Empty embedding returned for chunk {Index}. Skipping embedding insert.", i + 1);
                                failedCount++;
                                continue;
                            }

                            await _db.InsertEmbedding(chunkId, emb);
                            _logger.LogInformation("Inserted embedding for chunk {Index}/{Total}, size: {Size} bytes", 
                                i + 1, chunks.Count, emb.Length);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("Embedding generation cancelled/timeout for chunk {Index}/{Total}. Continuing...", 
                                i + 1, chunks.Count);
                            failedCount++;
                            // Continue - chunk is inserted, just missing embedding
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to create/insert embedding for chunk {Index}/{Total}. Skipping embedding.", 
                                i + 1, chunks.Count);
                            failedCount++;
                            // Continue - chunk is inserted, just missing embedding
                        }

                        processedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error processing chunk {Index}/{Total}. Continuing with next chunk.", 
                            i + 1, chunks.Count);
                        failedCount++;
                        // Continue processing other chunks even if one fails completely
                    }
                }

                // Increased throttle time between batches for better stability
                if (batchEnd < chunks.Count)
                {
                    const int delaySeconds = 4; // Increased from 3s to 4s
                    _logger.LogInformation("Batch {BatchNum}/{TotalBatches} complete. Throttling for {Delay} seconds before next batch...", 
                        batchNumber, totalBatches, delaySeconds);
                    await Task.Delay(delaySeconds * 1000);
                }
            }

            _logger.LogInformation("Chunk processing complete: {Processed} processed, {Failed} failed, {Total} total", 
                processedCount, failedCount, totalChunks);

            return processedCount;
        }

        private static int EstimateTokenCount(string text) =>
            string.IsNullOrWhiteSpace(text) ? 0 : (int)Math.Ceiling(text.Length / 4.0);

        private static string GenerateTopicTitle(string chunk, int index)
        {
            if (string.IsNullOrWhiteSpace(chunk)) return $"Chunk {index + 1}";
            int sentenceEnd = chunk.IndexOf('.');
            if (sentenceEnd > 0 && sentenceEnd < 100) return chunk[..sentenceEnd].Trim();
            return chunk[..Math.Min(100, chunk.Length)].Trim() + "...";
        }

        private static string GenerateSummary(string chunk)
        {
            if (string.IsNullOrWhiteSpace(chunk)) return "";
            return chunk[..Math.Min(250, chunk.Length)].Trim() + "...";
        }
    }
}
