using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// API endpoint to retrieve full evaluation results from blob storage.
    /// This is the primary endpoint for mobile apps to fetch detailed evaluation results.
    /// 
    /// Flow:
    /// 1. Mobile app calls GET /submissions/{id} to check status
    /// 2. When status = "Completed", call GET /submissions/{id}/result to get full evaluation
    /// </summary>
    public class GetEvaluationFromBlob
    {
        private readonly ILogger<GetEvaluationFromBlob> _logger;
        private readonly string _connectionString;
        private readonly string _storageConnectionString;

        public GetEvaluationFromBlob(ILogger<GetEvaluationFromBlob> logger)
        {
            _logger = logger;
            _connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
                ?? Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("AzureSqlConnectionString")
                ?? "";
            _storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
                ?? Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
                ?? "";
        }

        /// <summary>
        /// Get full evaluation result from blob storage
        /// GET /submissions/{submissionId}/result
        /// 
        /// Returns the complete evaluation JSON including:
        /// - Total score and percentage
        /// - Per-question evaluations with rubric breakdowns
        /// - Student answers, model answers, and feedback
        /// </summary>
        [Function("GetEvaluationFromBlob")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "submissions/{submissionId}/result")] HttpRequest req,
            string submissionId,
            CancellationToken ct)
        {
            _logger.LogInformation("GetEvaluationFromBlob: {SubmissionId}", submissionId);

            try
            {
                if (!Guid.TryParse(submissionId, out var id))
                {
                    return new BadRequestObjectResult(new { 
                        success = false,
                        error = "Invalid submissionId format. Must be a valid GUID." 
                    });
                }

                if (string.IsNullOrEmpty(_connectionString))
                {
                    return new ObjectResult(new { 
                        success = false,
                        error = "Database not configured" 
                    }) { StatusCode = 503 };
                }

                if (string.IsNullOrEmpty(_storageConnectionString))
                {
                    return new ObjectResult(new { 
                        success = false,
                        error = "Storage not configured" 
                    }) { StatusCode = 503 };
                }

                // Get blob path from database
                var (blobPath, status, errorMessage) = await GetBlobPathAsync(id, ct);

                if (blobPath == null && status == -1)
                {
                    return new NotFoundObjectResult(new { 
                        success = false,
                        error = "Submission not found", 
                        submissionId = submissionId 
                    });
                }

                if (status != 3) // Not Completed
                {
                    var statusText = status switch
                    {
                        0 => "Uploaded",
                        1 => "OCR Processing",
                        2 => "Evaluating",
                        4 => "Failed",
                        _ => "Unknown"
                    };

                    return new ObjectResult(new { 
                        success = false,
                        error = $"Evaluation not ready. Current status: {statusText}",
                        status = statusText,
                        statusCode = status,
                        errorMessage = status == 4 ? errorMessage : null
                    }) { StatusCode = status == 4 ? 400 : 202 }; // 202 Accepted = still processing
                }

                if (string.IsNullOrEmpty(blobPath))
                {
                    return new ObjectResult(new { 
                        success = false,
                        error = "Evaluation completed but result file not found",
                        submissionId = submissionId
                    }) { StatusCode = 500 };
                }

                // Read from blob storage
                var evaluationJson = await ReadBlobAsync(blobPath, ct);

                if (evaluationJson == null)
                {
                    return new NotFoundObjectResult(new { 
                        success = false,
                        error = "Evaluation result blob not found",
                        blobPath = blobPath
                    });
                }

                // Parse and transform to UI format
                try
                {
                    var blobData = JsonSerializer.Deserialize<JsonElement>(evaluationJson);
                    var uiResult = TransformToUiFormat(blobData);
                    
                    return new OkObjectResult(new {
                        success = true,
                        submissionId = submissionId,
                        result = uiResult
                    });
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse evaluation JSON, returning raw");
                    // Return as raw string if not valid JSON
                    return new OkObjectResult(new {
                        success = true,
                        submissionId = submissionId,
                        rawResult = evaluationJson
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting evaluation from blob: {SubmissionId}", submissionId);
                return new ObjectResult(new { 
                    success = false,
                    error = "Failed to get evaluation result", 
                    details = ex.Message 
                }) { StatusCode = 500 };
            }
        }

        private async Task<(string? blobPath, int status, string? errorMessage)> GetBlobPathAsync(Guid id, CancellationToken ct)
        {
            const string sql = @"
                SELECT EvaluationResultBlobPath, Status, ErrorMessage
                FROM WrittenSubmissions
                WHERE Id = @Id";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);

            await using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                var blobPath = reader.IsDBNull(0) ? null : reader.GetString(0);
                var status = reader.GetInt32(1);
                var errorMessage = reader.IsDBNull(2) ? null : reader.GetString(2);
                return (blobPath, status, errorMessage);
            }

            return (null, -1, null); // Not found
        }

        private async Task<string?> ReadBlobAsync(string blobPath, CancellationToken ct)
        {
            try
            {
                // blobPath format: "evaluation-results/{examId}/{submissionId}/evaluation-result.json"
                // OR could be full path like "https://storage.blob.core.windows.net/container/path"
                
                string containerName;
                string blobName;

                if (blobPath.StartsWith("http"))
                {
                    // Parse URL
                    var uri = new Uri(blobPath);
                    var pathParts = uri.AbsolutePath.TrimStart('/').Split('/', 2);
                    containerName = pathParts[0];
                    blobName = pathParts.Length > 1 ? pathParts[1] : "";
                }
                else
                {
                    // Parse path format: "container/blob/path"
                    var firstSlash = blobPath.IndexOf('/');
                    if (firstSlash > 0)
                    {
                        containerName = blobPath.Substring(0, firstSlash);
                        blobName = blobPath.Substring(firstSlash + 1);
                    }
                    else
                    {
                        containerName = "evaluation-results";
                        blobName = blobPath;
                    }
                }

                _logger.LogInformation("Reading blob: Container={Container}, Blob={Blob}", containerName, blobName);

                var blobServiceClient = new BlobServiceClient(_storageConnectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                if (!await blobClient.ExistsAsync(ct))
                {
                    _logger.LogWarning("Blob not found: {BlobPath}", blobPath);
                    return null;
                }

                var response = await blobClient.DownloadContentAsync(ct);
                return response.Value.Content.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading blob: {BlobPath}", blobPath);
                throw;
            }
        }

        /// <summary>
        /// Transform blob evaluation data to UI-friendly format
        /// </summary>
        private EvaluationResultDto TransformToUiFormat(JsonElement blobData)
        {
            var result = new EvaluationResultDto
            {
                ExamId = GetStringProperty(blobData, "examId", "ExamId"),
                StudentId = GetStringProperty(blobData, "studentId", "StudentId"),
                ExamTitle = GetStringProperty(blobData, "examTitle", "ExamTitle") ?? "",
                EvaluatedAt = GetDateTimeProperty(blobData, "evaluatedAt", "EvaluatedAt")
            };

            // Get question evaluations array
            var questionEvaluations = new List<JsonElement>();
            if (blobData.TryGetProperty("questionEvaluations", out var qe) || 
                blobData.TryGetProperty("QuestionEvaluations", out qe))
            {
                foreach (var q in qe.EnumerateArray())
                {
                    questionEvaluations.Add(q);
                }
            }

            // Separate MCQ and Subjective questions
            foreach (var q in questionEvaluations)
            {
                var isMcq = GetBoolProperty(q, "isMcq", "IsMcq");
                
                if (isMcq)
                {
                    var mcqResult = TransformToMcqResult(q);
                    result.McqResults.Add(mcqResult);
                    result.McqScore += mcqResult.MarksAwarded;
                    result.McqTotalMarks += mcqResult.MaxMarks;
                }
                else
                {
                    var subjectiveResult = TransformToSubjectiveResult(q);
                    result.SubjectiveResults.Add(subjectiveResult);
                    result.SubjectiveScore += subjectiveResult.EarnedMarks;
                    result.SubjectiveTotalMarks += subjectiveResult.MaxMarks;
                }
            }

            // Override MCQ scores from blob if available (more accurate)
            if (blobData.TryGetProperty("mcqScore", out var mcqScoreEl) || 
                blobData.TryGetProperty("McqScore", out mcqScoreEl))
            {
                result.McqScore = mcqScoreEl.GetDecimal();
            }
            if (blobData.TryGetProperty("mcqMaxScore", out var mcqMaxEl) || 
                blobData.TryGetProperty("McqMaxScore", out mcqMaxEl))
            {
                result.McqTotalMarks = mcqMaxEl.GetDecimal();
            }

            // Override subjective scores from blob if available
            if (blobData.TryGetProperty("subjectiveScore", out var subScoreEl) || 
                blobData.TryGetProperty("SubjectiveScore", out subScoreEl))
            {
                result.SubjectiveScore = subScoreEl.GetDecimal();
            }
            if (blobData.TryGetProperty("subjectiveMaxScore", out var subMaxEl) || 
                blobData.TryGetProperty("SubjectiveMaxScore", out subMaxEl))
            {
                result.SubjectiveTotalMarks = subMaxEl.GetDecimal();
            }

            // Calculate grand totals
            result.GrandScore = result.McqScore + result.SubjectiveScore;
            result.GrandTotalMarks = result.McqTotalMarks + result.SubjectiveTotalMarks;
            
            // Use blob percentage/grade if available, otherwise calculate
            if (blobData.TryGetProperty("percentage", out var pctEl) || 
                blobData.TryGetProperty("Percentage", out pctEl))
            {
                result.Percentage = pctEl.GetDecimal();
            }
            else if (result.GrandTotalMarks > 0)
            {
                result.Percentage = Math.Round((result.GrandScore / result.GrandTotalMarks) * 100, 2);
            }

            if (blobData.TryGetProperty("grade", out var gradeEl) || 
                blobData.TryGetProperty("Grade", out gradeEl))
            {
                result.Grade = gradeEl.GetString() ?? "";
            }
            else
            {
                result.Grade = CalculateGrade(result.Percentage);
            }

            result.Passed = result.Percentage >= 35; // Standard pass threshold

            // Sort results by question number
            result.McqResults.Sort((a, b) => a.QuestionNumber.CompareTo(b.QuestionNumber));
            result.SubjectiveResults.Sort((a, b) => a.QuestionNumber.CompareTo(b.QuestionNumber));

            return result;
        }

        private McqResultDto TransformToMcqResult(JsonElement q)
        {
            var awardedScore = GetDecimalProperty(q, "awardedScore", "AwardedScore");
            var maxScore = GetDecimalProperty(q, "maxScore", "MaxScore");
            
            return new McqResultDto
            {
                QuestionId = GetStringProperty(q, "questionId", "QuestionId") ?? "",
                QuestionNumber = GetIntProperty(q, "questionNumber", "QuestionNumber"),
                QuestionText = GetStringProperty(q, "questionText", "QuestionText") ?? "",
                SelectedOption = GetStringProperty(q, "extractedAnswer", "ExtractedAnswer") ?? "",
                CorrectAnswer = GetStringProperty(q, "modelAnswer", "ModelAnswer") ?? "",
                IsCorrect = awardedScore >= maxScore && maxScore > 0,
                MarksAwarded = awardedScore,
                MaxMarks = maxScore > 0 ? maxScore : 1,
                Options = GetOptionsFromQuestion(q)
            };
        }

        private SubjectiveResultDto TransformToSubjectiveResult(JsonElement q)
        {
            var earnedMarks = GetDecimalProperty(q, "awardedScore", "AwardedScore");
            var maxMarks = GetDecimalProperty(q, "maxScore", "MaxScore");
            
            var result = new SubjectiveResultDto
            {
                QuestionId = GetStringProperty(q, "questionId", "QuestionId") ?? "",
                QuestionNumber = GetIntProperty(q, "questionNumber", "QuestionNumber"),
                QuestionText = GetStringProperty(q, "questionText", "QuestionText") ?? "",
                EarnedMarks = earnedMarks,
                MaxMarks = maxMarks,
                IsFullyCorrect = earnedMarks >= maxMarks && maxMarks > 0,
                ExpectedAnswer = GetStringProperty(q, "modelAnswer", "ModelAnswer") ?? "",
                StudentAnswerEcho = GetStringProperty(q, "extractedAnswer", "ExtractedAnswer") ?? "",
                OverallFeedback = GetStringProperty(q, "feedback", "Feedback") ?? ""
            };

            // Parse rubric breakdown into step analysis
            var rubricBreakdown = GetStringProperty(q, "rubricBreakdown", "RubricBreakdown");
            if (!string.IsNullOrEmpty(rubricBreakdown))
            {
                result.StepAnalysis = ParseRubricToStepAnalysis(rubricBreakdown);
            }

            return result;
        }

        private List<StepAnalysisDto> ParseRubricToStepAnalysis(string rubricJson)
        {
            var steps = new List<StepAnalysisDto>();
            
            if (string.IsNullOrWhiteSpace(rubricJson))
                return steps;

            try
            {
                var rubricData = JsonSerializer.Deserialize<JsonElement>(rubricJson);
                
                // Handle array of steps
                if (rubricData.ValueKind == JsonValueKind.Array)
                {
                    int stepNum = 1;
                    foreach (var step in rubricData.EnumerateArray())
                    {
                        var marksAwarded = GetDecimalProperty(step, "awardedMarks", "marksAwarded", "marks");
                        var maxMarks = GetDecimalProperty(step, "maxMarks", "maxMarksForStep", "totalMarks");
                        
                        steps.Add(new StepAnalysisDto
                        {
                            Step = GetIntProperty(step, "stepNumber", "step") > 0 
                                ? GetIntProperty(step, "stepNumber", "step") 
                                : stepNum,
                            Description = GetStringProperty(step, "description", "stepDescription", "criterion") ?? "",
                            IsCorrect = marksAwarded >= maxMarks && maxMarks > 0,
                            MarksAwarded = marksAwarded,
                            MaxMarksForStep = maxMarks,
                            Feedback = GetStringProperty(step, "reason", "feedback", "comment") ?? ""
                        });
                        stepNum++;
                    }
                }
                // Handle object with steps array
                else if (rubricData.ValueKind == JsonValueKind.Object)
                {
                    if (rubricData.TryGetProperty("steps", out var stepsArray))
                    {
                        return ParseRubricToStepAnalysis(stepsArray.GetRawText());
                    }
                }
            }
            catch (JsonException)
            {
                // Not valid JSON, ignore
            }

            return steps;
        }

        private List<string> GetOptionsFromQuestion(JsonElement q)
        {
            var options = new List<string>();
            
            if (q.TryGetProperty("options", out var optionsEl) || 
                q.TryGetProperty("Options", out optionsEl) ||
                q.TryGetProperty("mcqOptions", out optionsEl))
            {
                if (optionsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var opt in optionsEl.EnumerateArray())
                    {
                        options.Add(opt.GetString() ?? "");
                    }
                }
            }
            
            return options;
        }

        private string CalculateGrade(decimal percentage)
        {
            return percentage switch
            {
                >= 90 => "A+",
                >= 80 => "A",
                >= 70 => "B+",
                >= 60 => "B",
                >= 50 => "C",
                >= 40 => "D",
                >= 35 => "E",
                _ => "F"
            };
        }

        // Helper methods for JSON property access with fallback names
        private string? GetStringProperty(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
            }
            return null;
        }

        private int GetIntProperty(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number)
                        return prop.GetInt32();
                    if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var val))
                        return val;
                }
            }
            return 0;
        }

        private decimal GetDecimalProperty(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number)
                        return prop.GetDecimal();
                    if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var val))
                        return val;
                }
            }
            return 0;
        }

        private bool GetBoolProperty(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.True) return true;
                    if (prop.ValueKind == JsonValueKind.False) return false;
                    if (prop.ValueKind == JsonValueKind.String)
                        return prop.GetString()?.ToLower() == "true";
                }
            }
            return false;
        }

        private DateTime GetDateTimeProperty(JsonElement el, params string[] names)
        {
            foreach (var name in names)
            {
                if (el.TryGetProperty(name, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.String && DateTime.TryParse(prop.GetString(), out var dt))
                        return dt;
                }
            }
            return DateTime.UtcNow;
        }
    }
}
