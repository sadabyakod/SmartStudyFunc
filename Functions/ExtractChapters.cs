using Azure.AI.OpenAI;
using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using SmartStudyFunc.Helpers;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Dapper;

namespace SmartStudyFunc.Functions
{
    public class UnitChapter
    {
        [JsonPropertyName("unit")]
        public string? Unit { get; set; }
        
        [JsonPropertyName("chapters")]
        public string[]? Chapters { get; set; }
    }

    public static class ExtractChapters
    {
        public static async Task ExtractChaptersFromSyllabusAsync(Stream pdfStream, string blobName, ILogger log)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                log.LogInformation("STEP-3: Extracting chapters from syllabus file: " + blobName);

                // Validate inputs
                if (pdfStream == null || !pdfStream.CanRead)
                {
                    log.LogError("PDF stream is null or cannot be read for: {BlobName}", blobName);
                    return;
                }

                if (string.IsNullOrWhiteSpace(blobName))
                {
                    log.LogError("Blob name is null or empty");
                    return;
                }

                // -----------------------------------------
                // 1. Convert stream to byte[] & extract text
                // -----------------------------------------
                byte[] fileBytes;
                try
                {
                    using var ms = new MemoryStream();
                    await pdfStream.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                    log.LogInformation("Successfully read PDF stream: {Size} bytes", fileBytes.Length);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to read PDF stream for: {BlobName}", blobName);
                    return;
                }

                if (fileBytes == null || fileBytes.Length == 0)
                {
                    log.LogWarning("PDF stream is empty for: {BlobName}", blobName);
                    return;
                }

                // Use your existing PdfPig extractor with error handling
                string text;
                try
                {
                    text = PdfTextExtractorHelper.Extract(fileBytes);
                    log.LogInformation("Extracted {Len} chars from syllabus PDF", text?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to extract text from PDF: {BlobName}", blobName);
                    return;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    log.LogWarning("No text extracted from PDF: {BlobName}", blobName);
                    return;
                }

                // -----------------------------------------
                // 2. Insert Syllabus record into SQL with retry
                // -----------------------------------------
                var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    log.LogError("SqlConnectionString not configured");
                    return;
                }

                int syllabusId = 0;
                const int maxSqlRetries = 3;
                const int sqlRetryDelayMs = 500;
                Exception? lastSqlException = null;
                bool syllabusInserted = false;

                for (int attempt = 1; attempt <= maxSqlRetries; attempt++)
                {
                    try
                    {
                        using var con = new SqlConnection(connectionString);
                        await con.OpenAsync();

                        syllabusId = await con.ExecuteScalarAsync<int>(@"
                            INSERT INTO Syllabus (FileName, RawText)
                            OUTPUT INSERTED.Id
                            VALUES (@FileName, @RawText)
                        ", new
                        {
                            FileName = blobName,
                            RawText = text
                        });

                        log.LogInformation("Inserted syllabus record with ID = {Id}", syllabusId);
                        syllabusInserted = true;
                        break; // Success - exit retry loop
                    }
                    catch (SqlException ex) when (attempt < maxSqlRetries)
                    {
                        lastSqlException = ex;
                        int delayMs = sqlRetryDelayMs * (int)Math.Pow(2, attempt - 1);
                        log.LogWarning("SQL error on attempt {Attempt}/{MaxRetries}: {Message}. Retrying in {Delay}ms...", 
                            attempt, maxSqlRetries, ex.Message, delayMs);
                        await Task.Delay(delayMs);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed to insert syllabus record for: {BlobName}", blobName);
                        return;
                    }
                }

                if (!syllabusInserted)
                {
                    log.LogError(lastSqlException, "Failed to insert syllabus after {MaxRetries} attempts", maxSqlRetries);
                    return;
                }

                // -----------------------------------------
                // 3. Use GPT to extract Units + Chapters as JSON
                // -----------------------------------------
                var endpoint = Environment.GetEnvironmentVariable("OpenAIEndpoint");
                var apiKey = Environment.GetEnvironmentVariable("OpenAIKey");
                
                if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
                {
                    log.LogError("OpenAI configuration not set (OpenAIEndpoint or OpenAIKey missing)");
                    return;
                }

                OpenAIClient openAi;
                try
                {
                    openAi = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to initialize OpenAI client");
                    return;
                }

                string prompt = $@"
Extract ONLY 'units' and 'chapters' from the following Karnataka PUC Mathematics syllabus.

Return STRICT JSON format:
[
  {{
    ""unit"": ""Unit Name"",
    ""chapters"": [""Chapter1"", ""Chapter2""]
  }}
]

Syllabus Text:
{text}
";

                var chatOptions = new ChatCompletionsOptions
                {
                    DeploymentName = "gpt-4o-mini",
                    Messages =
                    {
                        new ChatRequestSystemMessage("You extract syllabus structure cleanly."),
                        new ChatRequestUserMessage(prompt)
                    }
                };

                string json = string.Empty;
                const int maxOpenAiRetries = 3;
                const int openAiRetryDelayMs = 1000;
                Exception? lastOpenAiException = null;
                bool openAiSuccess = false;

                for (int attempt = 1; attempt <= maxOpenAiRetries; attempt++)
                {
                    try
                    {
                        var resp = await openAi.GetChatCompletionsAsync(chatOptions);
                        
                        if (resp?.Value?.Choices == null || resp.Value.Choices.Count == 0)
                        {
                            log.LogWarning("OpenAI returned no choices on attempt {Attempt}", attempt);
                            if (attempt < maxOpenAiRetries)
                            {
                                await Task.Delay(openAiRetryDelayMs * attempt);
                                continue;
                            }
                            return;
                        }

                        json = resp.Value.Choices[0].Message.Content;
                        log.LogInformation("GPT returned syllabus structure JSON: {Length} chars", json?.Length ?? 0);
                        openAiSuccess = true;
                        break; // Success - exit retry loop
                    }
                    catch (RequestFailedException ex) when (attempt < maxOpenAiRetries && (ex.Status == 429 || ex.Status >= 500))
                    {
                        lastOpenAiException = ex;
                        int delayMs = openAiRetryDelayMs * (int)Math.Pow(2, attempt - 1);
                        log.LogWarning("OpenAI transient error on attempt {Attempt}/{MaxRetries}: {Message}. Retrying in {Delay}ms...", 
                            attempt, maxOpenAiRetries, ex.Message, delayMs);
                        await Task.Delay(delayMs);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed to get chat completion from OpenAI");
                        return;
                    }
                }

                if (!openAiSuccess || string.IsNullOrWhiteSpace(json))
                {
                    log.LogError(lastOpenAiException, "Failed to get OpenAI response after {MaxRetries} attempts", maxOpenAiRetries);
                    return;
                }

                // -----------------------------------------
                // 4. Parse JSON and Insert Units & Chapters into SQL
                // -----------------------------------------
                UnitChapter[]? unitList = null;
                try
                {
                    unitList = JsonSerializer.Deserialize<UnitChapter[]>(json);
                }
                catch (JsonException ex)
                {
                    log.LogError(ex, "Failed to deserialize JSON from GPT. JSON: {Json}", json);
                    return;
                }

                if (unitList == null || unitList.Length == 0)
                {
                    log.LogWarning("No units found in GPT response");
                    return;
                }

                // Insert units and chapters with retry logic
                int insertedCount = 0;
                foreach (var u in unitList)
                {
                    string unitName = u.Unit ?? "Unknown Unit";

                    if (u.Chapters != null)
                    {
                        foreach (var chapter in u.Chapters)
                        {
                            if (string.IsNullOrWhiteSpace(chapter))
                            {
                                log.LogWarning("Skipping empty chapter in unit: {Unit}", unitName);
                                continue;
                            }

                            for (int attempt = 1; attempt <= maxSqlRetries; attempt++)
                            {
                                try
                                {
                                    using var con = new SqlConnection(connectionString);
                                    await con.OpenAsync();

                                    await con.ExecuteAsync(@"
                                        INSERT INTO Chapters (SyllabusId, UnitName, ChapterName)
                                        VALUES (@SyllabusId, @UnitName, @ChapterName)
                                    ", new
                                    {
                                        SyllabusId = syllabusId,
                                        UnitName = unitName,
                                        ChapterName = chapter
                                    });

                                    log.LogInformation("Inserted chapter: {Unit} -> {Chapter}", unitName, chapter);
                                    insertedCount++;
                                    break; // Success - exit retry loop
                                }
                                catch (SqlException sqlEx) when (attempt < maxSqlRetries)
                                {
                                    int delayMs = sqlRetryDelayMs * (int)Math.Pow(2, attempt - 1);
                                    log.LogWarning(sqlEx, "SQL error inserting chapter on attempt {Attempt}/{MaxRetries}. Retrying in {Delay}ms...", 
                                        attempt, maxSqlRetries, delayMs);
                                    await Task.Delay(delayMs);
                                }
                                catch (Exception ex)
                                {
                                    log.LogError(ex, "Failed to insert chapter: {Unit} -> {Chapter}", unitName, chapter);
                                    break; // Move to next chapter
                                }
                            }
                        }
                    }
                }

                var duration = DateTime.UtcNow - startTime;
                log.LogInformation("STEP-3 COMPLETE: {Count} chapters extracted & stored. Duration: {Duration:mm\\:ss}", 
                    insertedCount, duration);
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                log.LogError(ex, "CRITICAL: Unexpected error in ExtractChaptersFromSyllabusAsync. Duration: {Duration:mm\\:ss}", duration);
                // Do NOT throw - log and exit gracefully
            }
        }
    }
}
