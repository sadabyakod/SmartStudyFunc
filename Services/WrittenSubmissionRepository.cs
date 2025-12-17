using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Repository for written submission database operations.
    /// All operations are async, non-blocking, and fault-tolerant.
    /// </summary>
    public interface IWrittenSubmissionRepository
    {
        Task<WrittenSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        
        Task UpdateStatusAsync(
            Guid id, 
            WrittenSubmissionStatus status, 
            string? errorMessage = null, 
            CancellationToken cancellationToken = default);
        
        Task SaveExtractedTextAsync(
            Guid id, 
            string extractedText, 
            string extractedTextJson,
            string? blobPath = null, 
            long? processingTimeMs = null,
            CancellationToken cancellationToken = default);
        
        Task SaveEvaluationResultAsync(
            WrittenEvaluationResult result,
            string? resultBlobPath = null,
            long? processingTimeMs = null,
            CancellationToken cancellationToken = default);
        
        Task<List<ExamQuestionWithRubric>> GetExamQuestionsWithRubricsAsync(
            string examId, 
            CancellationToken cancellationToken = default);
        
        Task<List<WrittenSubmission>> GetOldSubmissionsAsync(
            int retentionDays, 
            CancellationToken cancellationToken = default);
        
        Task MarkBlobsDeletedAsync(Guid submissionId, CancellationToken cancellationToken = default);
        
        Task IncrementRetryCountAsync(Guid id, CancellationToken cancellationToken = default);
    }

    public class WrittenSubmissionRepository : IWrittenSubmissionRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<WrittenSubmissionRepository> _logger;

        public WrittenSubmissionRepository(
            string connectionString,
            ILogger<WrittenSubmissionRepository> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        public async Task<WrittenSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT Id, ExamId, StudentId, FilePaths, Status, 
                       ExtractedText, ExtractedTextJson, ExtractedTextBlobPath,
                       TotalScore, MaxPossibleScore, Percentage, Grade,
                       ErrorMessage, SubmittedAt, OcrStartedAt, OcrCompletedAt,
                       EvaluationStartedAt, EvaluatedAt, RetryCount,
                       OcrProcessingTimeMs, EvaluationProcessingTimeMs
                FROM WrittenSubmissions
                WHERE Id = @Id";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            
            if (await reader.ReadAsync(cancellationToken))
            {
                return MapSubmissionFromReader(reader);
            }

            return null;
        }

        private static WrittenSubmission MapSubmissionFromReader(SqlDataReader reader)
        {
            return new WrittenSubmission
            {
                Id = reader.GetGuid(0),
                ExamId = reader.GetString(1),
                StudentId = reader.GetString(2),
                FilePaths = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? new(),
                Status = (WrittenSubmissionStatus)reader.GetInt32(4),
                ExtractedText = reader.IsDBNull(5) ? null : reader.GetString(5),
                ExtractedTextJson = reader.IsDBNull(6) ? null : reader.GetString(6),
                ExtractedTextBlobPath = reader.IsDBNull(7) ? null : reader.GetString(7),
                TotalScore = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                MaxPossibleScore = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                Percentage = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                Grade = reader.IsDBNull(11) ? null : reader.GetString(11),
                ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
                SubmittedAt = reader.GetDateTime(13),
                OcrStartedAt = reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                OcrCompletedAt = reader.IsDBNull(15) ? null : reader.GetDateTime(15),
                EvaluationStartedAt = reader.IsDBNull(16) ? null : reader.GetDateTime(16),
                EvaluatedAt = reader.IsDBNull(17) ? null : reader.GetDateTime(17),
                RetryCount = reader.GetInt32(18),
                OcrProcessingTimeMs = reader.IsDBNull(19) ? null : reader.GetInt64(19),
                EvaluationProcessingTimeMs = reader.IsDBNull(20) ? null : reader.GetInt64(20)
            };
        }

        public async Task UpdateStatusAsync(
            Guid id, 
            WrittenSubmissionStatus status, 
            string? errorMessage = null,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE WrittenSubmissions
                SET Status = @Status,
                    ErrorMessage = COALESCE(@ErrorMessage, ErrorMessage),
                    OcrStartedAt = CASE WHEN @Status = @OcrProcessingStatus AND OcrStartedAt IS NULL THEN GETUTCDATE() ELSE OcrStartedAt END,
                    OcrCompletedAt = CASE WHEN @Status = @EvaluatingStatus THEN GETUTCDATE() ELSE OcrCompletedAt END,
                    EvaluationStartedAt = CASE WHEN @Status = @EvaluatingStatus AND EvaluationStartedAt IS NULL THEN GETUTCDATE() ELSE EvaluationStartedAt END,
                    EvaluatedAt = CASE WHEN @Status = @CompletedStatus THEN GETUTCDATE() ELSE EvaluatedAt END
                WHERE Id = @Id";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@Status", (int)status);
            command.Parameters.AddWithValue("@ErrorMessage", (object?)errorMessage ?? DBNull.Value);
            command.Parameters.AddWithValue("@OcrProcessingStatus", (int)WrittenSubmissionStatus.OcrProcessing);
            command.Parameters.AddWithValue("@EvaluatingStatus", (int)WrittenSubmissionStatus.Evaluating);
            command.Parameters.AddWithValue("@CompletedStatus", (int)WrittenSubmissionStatus.Completed);

            await command.ExecuteNonQueryAsync(cancellationToken);
            
            _logger.LogDebug("[{SubmissionId}] Status updated to {Status}", id, status);
        }

        public async Task IncrementRetryCountAsync(Guid id, CancellationToken cancellationToken = default)
        {
            const string sql = "UPDATE WrittenSubmissions SET RetryCount = RetryCount + 1 WHERE Id = @Id";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task SaveExtractedTextAsync(
            Guid id, 
            string extractedText,
            string extractedTextJson,
            string? blobPath = null,
            long? processingTimeMs = null,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE WrittenSubmissions
                SET ExtractedText = @ExtractedText,
                    ExtractedTextJson = @ExtractedTextJson,
                    ExtractedTextBlobPath = @BlobPath,
                    OcrCompletedAt = GETUTCDATE(),
                    OcrProcessingTimeMs = @ProcessingTimeMs
                WHERE Id = @Id";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@ExtractedText", extractedText);
            command.Parameters.AddWithValue("@ExtractedTextJson", extractedTextJson);
            command.Parameters.AddWithValue("@BlobPath", (object?)blobPath ?? DBNull.Value);
            command.Parameters.AddWithValue("@ProcessingTimeMs", (object?)processingTimeMs ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug(
                "[{SubmissionId}] Extracted text saved ({Length} chars, {TimeMs}ms)",
                id, extractedText.Length, processingTimeMs);
        }

        public async Task SaveEvaluationResultAsync(
            WrittenEvaluationResult result,
            string? resultBlobPath = null,
            long? processingTimeMs = null,
            CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction();

            try
            {
                // Update submission with final scores and result blob path
                const string updateSubmissionSql = @"
                    UPDATE WrittenSubmissions
                    SET Status = @Status,
                        EvaluatedAt = @EvaluatedAt,
                        TotalScore = @TotalScore,
                        MaxPossibleScore = @MaxPossibleScore,
                        Percentage = @Percentage,
                        Grade = @Grade,
                        EvaluationResultBlobPath = @ResultBlobPath,
                        EvaluationProcessingTimeMs = @ProcessingTimeMs
                    WHERE Id = @Id";

                await using (var cmd = new SqlCommand(updateSubmissionSql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@Id", result.WrittenSubmissionId);
                    cmd.Parameters.AddWithValue("@Status", (int)WrittenSubmissionStatus.Completed);
                    cmd.Parameters.AddWithValue("@EvaluatedAt", result.EvaluatedAt);
                    cmd.Parameters.AddWithValue("@TotalScore", result.TotalScore);
                    cmd.Parameters.AddWithValue("@MaxPossibleScore", result.MaxPossibleScore);
                    cmd.Parameters.AddWithValue("@Percentage", result.Percentage);
                    cmd.Parameters.AddWithValue("@Grade", CalculateGrade(result.Percentage));
                    cmd.Parameters.AddWithValue("@ResultBlobPath", (object?)resultBlobPath ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@ProcessingTimeMs", (object?)processingTimeMs ?? DBNull.Value);
                    
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Insert individual question evaluations
                const string insertEvaluationSql = @"
                    INSERT INTO WrittenQuestionEvaluations 
                        (Id, WrittenSubmissionId, QuestionId, QuestionNumber, 
                         ExtractedAnswer, ModelAnswer, MaxScore, AwardedScore, 
                         Feedback, RubricBreakdown, EvaluatedAt)
                    VALUES 
                        (@Id, @WrittenSubmissionId, @QuestionId, @QuestionNumber,
                         @ExtractedAnswer, @ModelAnswer, @MaxScore, @AwardedScore,
                         @Feedback, @RubricBreakdown, @EvaluatedAt)";

                foreach (var eval in result.QuestionEvaluations)
                {
                    await using var cmd = new SqlCommand(insertEvaluationSql, connection, transaction);
                    cmd.Parameters.AddWithValue("@Id", eval.Id);
                    cmd.Parameters.AddWithValue("@WrittenSubmissionId", eval.WrittenSubmissionId);
                    cmd.Parameters.AddWithValue("@QuestionId", eval.QuestionId);
                    cmd.Parameters.AddWithValue("@QuestionNumber", eval.QuestionNumber);
                    cmd.Parameters.AddWithValue("@ExtractedAnswer", eval.ExtractedAnswer);
                    cmd.Parameters.AddWithValue("@ModelAnswer", eval.ModelAnswer);
                    cmd.Parameters.AddWithValue("@MaxScore", eval.MaxScore);
                    cmd.Parameters.AddWithValue("@AwardedScore", eval.AwardedScore);
                    cmd.Parameters.AddWithValue("@Feedback", eval.Feedback);
                    cmd.Parameters.AddWithValue("@RubricBreakdown", eval.RubricBreakdown);
                    cmd.Parameters.AddWithValue("@EvaluatedAt", eval.EvaluatedAt);

                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "[{SubmissionId}] Evaluation results saved: {Score}/{MaxScore}",
                    result.WrittenSubmissionId, result.TotalScore, result.MaxPossibleScore);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task<List<ExamQuestionWithRubric>> GetExamQuestionsWithRubricsAsync(
            string examId,
            CancellationToken cancellationToken = default)
        {
            var questions = new List<ExamQuestionWithRubric>();

            // First try ExamQuestions table (legacy/manual questions)
            questions = await GetQuestionsFromExamQuestionsTableAsync(examId, cancellationToken);
            
            if (questions.Count > 0)
            {
                _logger.LogInformation("Found {Count} questions in ExamQuestions table for exam {ExamId}", 
                    questions.Count, examId);
                return questions;
            }

            // Fallback to GeneratedExams table (AI-generated exams from backend)
            _logger.LogInformation("No questions in ExamQuestions table, checking GeneratedExams for {ExamId}", examId);
            questions = await GetQuestionsFromGeneratedExamsTableAsync(examId, cancellationToken);
            
            if (questions.Count > 0)
            {
                _logger.LogInformation("Found {Count} questions in GeneratedExams table for exam {ExamId}", 
                    questions.Count, examId);
            }
            else
            {
                _logger.LogWarning("No questions found in either table for exam {ExamId}", examId);
            }

            return questions;
        }

        private async Task<List<ExamQuestionWithRubric>> GetQuestionsFromExamQuestionsTableAsync(
            string examId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT q.Id, q.QuestionNumber, q.QuestionText, q.ModelAnswer,
                       q.MaxScore, q.Rubric, q.Keywords,
                       q.ClassName, q.Subject, q.Chapter
                FROM ExamQuestions q
                WHERE q.ExamId = @ExamId
                ORDER BY q.QuestionNumber";

            var questions = new List<ExamQuestionWithRubric>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 30;
            command.Parameters.AddWithValue("@ExamId", examId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var keywordsJson = reader.IsDBNull(6) ? "[]" : reader.GetString(6);
                
                questions.Add(new ExamQuestionWithRubric
                {
                    QuestionId = reader.GetGuid(0).ToString(),
                    QuestionNumber = reader.GetInt32(1),
                    QuestionText = reader.GetString(2),
                    ModelAnswer = reader.GetString(3),
                    MaxScore = reader.GetDecimal(4),
                    Rubric = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Keywords = JsonSerializer.Deserialize<List<string>>(keywordsJson) ?? new(),
                    ClassName = reader.IsDBNull(7) ? null : reader.GetString(7),
                    Subject = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Chapter = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }

            return questions;
        }

        private async Task<List<ExamQuestionWithRubric>> GetQuestionsFromGeneratedExamsTableAsync(
            string examId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT ExamContentJson, Subject, Grade, Chapter
                FROM GeneratedExams
                WHERE ExamId = @ExamId AND IsActive = 1";

            var questions = new List<ExamQuestionWithRubric>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            command.CommandTimeout = 30;
            command.Parameters.AddWithValue("@ExamId", examId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                var examContentJson = reader.GetString(0);
                var subject = reader.IsDBNull(1) ? null : reader.GetString(1);
                var grade = reader.IsDBNull(2) ? null : reader.GetString(2);
                var chapter = reader.IsDBNull(3) ? null : reader.GetString(3);

                // Parse the JSON content
                try
                {
                    using var doc = JsonDocument.Parse(examContentJson);
                    var root = doc.RootElement;

                    // Check if exam has "parts" structure (multi-part exam)
                    if (root.TryGetProperty("parts", out var partsElement))
                    {
                        foreach (var part in partsElement.EnumerateArray())
                        {
                            var partName = part.TryGetProperty("partName", out var pn) ? pn.GetString() : "";
                            var marksPerQuestion = part.TryGetProperty("marksPerQuestion", out var mpq) ? mpq.GetInt32() : 1;
                            
                            if (part.TryGetProperty("questions", out var questionsElement))
                            {
                                foreach (var q in questionsElement.EnumerateArray())
                                {
                                    var question = ParseQuestionFromJson(q, subject, grade, chapter, marksPerQuestion);
                                    if (question != null)
                                    {
                                        questions.Add(question);
                                    }
                                }
                            }
                        }
                    }
                    // Check if exam has flat "questions" array
                    else if (root.TryGetProperty("questions", out var questionsElement))
                    {
                        foreach (var q in questionsElement.EnumerateArray())
                        {
                            var question = ParseQuestionFromJson(q, subject, grade, chapter, defaultMarks: 5);
                            if (question != null)
                            {
                                questions.Add(question);
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to parse ExamContentJson for exam {ExamId}", examId);
                }
            }

            return questions;
        }

        private ExamQuestionWithRubric? ParseQuestionFromJson(
            JsonElement q, 
            string? subject, 
            string? grade, 
            string? chapter,
            int defaultMarks)
        {
            try
            {
                var questionId = q.TryGetProperty("questionId", out var qid) ? qid.GetString() ?? "" : Guid.NewGuid().ToString();
                var questionNumber = q.TryGetProperty("questionNumber", out var qn) ? qn.GetInt32() : 0;
                var questionText = q.TryGetProperty("questionText", out var qt) ? qt.GetString() ?? "" : "";
                var correctAnswer = q.TryGetProperty("correctAnswer", out var ca) ? ca.GetString() ?? "" : "";
                var topic = q.TryGetProperty("topic", out var t) ? t.GetString() ?? "" : "";
                var marks = q.TryGetProperty("marks", out var m) ? m.GetInt32() : defaultMarks;

                // Skip if no question text
                if (string.IsNullOrWhiteSpace(questionText))
                {
                    return null;
                }

                // Build rubric from topic if available
                var rubric = !string.IsNullOrEmpty(topic) 
                    ? $"Topic: {topic}. Evaluate based on correct answer and understanding of concepts."
                    : "Evaluate based on correct answer and understanding of concepts.";

                // Extract keywords from topic and answer
                var keywords = new List<string>();
                if (!string.IsNullOrEmpty(topic))
                {
                    keywords.Add(topic);
                }

                return new ExamQuestionWithRubric
                {
                    QuestionId = questionId,
                    QuestionNumber = questionNumber,
                    QuestionText = questionText,
                    ModelAnswer = correctAnswer,
                    MaxScore = marks,
                    Rubric = rubric,
                    Keywords = keywords,
                    ClassName = grade,
                    Subject = subject,
                    Chapter = chapter ?? topic
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse question from JSON");
                return null;
            }
        }

        public async Task<List<WrittenSubmission>> GetOldSubmissionsAsync(
            int retentionDays,
            CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT Id, ExamId, StudentId, FilePaths, Status,
                       ExtractedText, ExtractedTextJson, ExtractedTextBlobPath,
                       TotalScore, MaxPossibleScore, Percentage, Grade,
                       ErrorMessage, SubmittedAt, OcrStartedAt, OcrCompletedAt,
                       EvaluationStartedAt, EvaluatedAt, RetryCount,
                       OcrProcessingTimeMs, EvaluationProcessingTimeMs
                FROM WrittenSubmissions
                WHERE SubmittedAt < DATEADD(DAY, -@RetentionDays, GETUTCDATE())
                  AND BlobsDeleted = 0
                  AND Status IN (@Completed, @Failed)";

            var submissions = new List<WrittenSubmission>();

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@RetentionDays", retentionDays);
            command.Parameters.AddWithValue("@Completed", (int)WrittenSubmissionStatus.Completed);
            command.Parameters.AddWithValue("@Failed", (int)WrittenSubmissionStatus.Failed);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                submissions.Add(MapSubmissionFromReader(reader));
            }

            return submissions;
        }

        public async Task MarkBlobsDeletedAsync(Guid submissionId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                UPDATE WrittenSubmissions
                SET BlobsDeleted = 1,
                    BlobsDeletedAt = GETUTCDATE()
                WHERE Id = @Id";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", submissionId);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>
        /// Calculate letter grade from percentage
        /// </summary>
        private static string CalculateGrade(decimal percentage)
        {
            return percentage switch
            {
                >= 90 => "A+",
                >= 80 => "A",
                >= 70 => "B+",
                >= 60 => "B",
                >= 50 => "C",
                >= 40 => "D",
                _ => "F"
            };
        }
    }
}
