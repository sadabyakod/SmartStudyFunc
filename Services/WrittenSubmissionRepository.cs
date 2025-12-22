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
        private readonly IRubricBlobService? _rubricBlobService;

        public WrittenSubmissionRepository(
            string connectionString,
            ILogger<WrittenSubmissionRepository> logger,
            IRubricBlobService? rubricBlobService = null)
        {
            _connectionString = connectionString;
            _logger = logger;
            _rubricBlobService = rubricBlobService;
        }

        public async Task<WrittenSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            const string sql = @"
                SELECT Id, ExamId, StudentId, FilePaths, Status, 
                       ExtractedText, ExtractedTextJson, ExtractedTextBlobPath,
                       TotalScore, MaxPossibleScore, Percentage, Grade,
                       ErrorMessage, SubmittedAt, OcrStartedAt, OcrCompletedAt,
                       EvaluationStartedAt, EvaluatedAt, RetryCount,
                       OcrProcessingTimeMs, EvaluationProcessingTimeMs,
                       McqAnswers, McqScore, McqTotalMarks
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
                EvaluationProcessingTimeMs = reader.IsDBNull(20) ? null : reader.GetInt64(20),
                // MCQ columns (may not exist in older databases)
                McqAnswers = reader.FieldCount > 21 && !reader.IsDBNull(21) ? reader.GetString(21) : null,
                McqScore = reader.FieldCount > 22 && !reader.IsDBNull(22) ? reader.GetDecimal(22) : null,
                McqTotalMarks = reader.FieldCount > 23 && !reader.IsDBNull(23) ? reader.GetDecimal(23) : null
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
            // CRITICAL: EvaluationResultBlobPath MUST be set when marking as Completed
            // This ensures mobile app always gets the blob path for completed submissions
            if (string.IsNullOrEmpty(resultBlobPath))
            {
                throw new ArgumentException(
                    $"EvaluationResultBlobPath is required when saving evaluation result. SubmissionId: {result.WrittenSubmissionId}",
                    nameof(resultBlobPath));
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = connection.BeginTransaction();

            try
            {
                // Update submission with final scores and result blob path (Status + BlobPath updated atomically)
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
                    cmd.Parameters.AddWithValue("@ResultBlobPath", resultBlobPath); // Required - validated above
                    cmd.Parameters.AddWithValue("@ProcessingTimeMs", (object?)processingTimeMs ?? DBNull.Value);
                    
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // All question evaluations are stored in blob (EvaluationResultBlobPath)
                // No need to duplicate in WrittenQuestionEvaluations table

                await transaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "[{SubmissionId}] Evaluation results saved atomically: Status=Completed, BlobPath={BlobPath}, Score={Score}/{MaxScore}",
                    result.WrittenSubmissionId, resultBlobPath, result.TotalScore, result.MaxPossibleScore);
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
            // Try V2 tables first (GeneratedExamPapers + GeneratedExamQuestions with blob rubrics)
            _logger.LogInformation("Loading questions for exam {ExamId}", examId);
            
            var questionsV2 = await GetQuestionsFromV2TablesAsync(examId, cancellationToken);
            if (questionsV2.Count > 0)
            {
                _logger.LogInformation("✓ Found {Count} questions in V2 tables (GeneratedExamQuestions) for exam {ExamId}", 
                    questionsV2.Count, examId);
                return questionsV2;
            }
            
            // Try SubjectiveRubrics table (backend's rubric lookup table)
            _logger.LogInformation("V2 tables empty, trying SubjectiveRubrics + GeneratedExams for exam {ExamId}", examId);
            var questionsWithSubjectiveRubrics = await GetQuestionsWithSubjectiveRubricsAsync(examId, cancellationToken);
            if (questionsWithSubjectiveRubrics.Count > 0)
            {
                _logger.LogInformation("✓ Found {Count} questions with SubjectiveRubrics for exam {ExamId}", 
                    questionsWithSubjectiveRubrics.Count, examId);
                return questionsWithSubjectiveRubrics;
            }
            
            // Fallback to GeneratedExams table only (no SubjectiveRubrics)
            _logger.LogInformation("SubjectiveRubrics empty, trying GeneratedExams table only for exam {ExamId}", examId);
            var questions = await GetQuestionsFromGeneratedExamsTableAsync(examId, cancellationToken);
            
            if (questions.Count > 0)
            {
                _logger.LogInformation("✓ Found {Count} questions in GeneratedExams table for exam {ExamId}", 
                    questions.Count, examId);
            }
            else
            {
                _logger.LogError("✗ No questions found in any table for exam {ExamId}", examId);
            }

            return questions;
        }
        
        /// <summary>
        /// Get questions from GeneratedExams table and load frozen rubrics from SubjectiveRubrics + Blob.
        /// This is the backend's pattern: SubjectiveRubrics is an index pointing to blob rubrics.
        /// </summary>
        private async Task<List<ExamQuestionWithRubric>> GetQuestionsWithSubjectiveRubricsAsync(
            string examId,
            CancellationToken cancellationToken)
        {
            // First, get the rubric mappings from SubjectiveRubrics table
            var rubricMappings = await GetSubjectiveRubricMappingsAsync(examId, cancellationToken);
            if (rubricMappings.Count == 0)
            {
                return new List<ExamQuestionWithRubric>();
            }
            
            // Load questions from GeneratedExams
            var questions = await GetQuestionsFromGeneratedExamsTableAsync(examId, cancellationToken);
            if (questions.Count == 0)
            {
                return new List<ExamQuestionWithRubric>();
            }
            
            // Enrich questions with frozen rubrics from blob storage
            foreach (var question in questions)
            {
                if (rubricMappings.TryGetValue(question.QuestionId, out var rubricInfo))
                {
                    // Load rubric from blob using the path from SubjectiveRubrics
                    if (!string.IsNullOrEmpty(rubricInfo.BlobPath) && _rubricBlobService != null)
                    {
                        try
                        {
                            var rubricFromBlob = await _rubricBlobService.GetRubricAsync(rubricInfo.BlobPath, cancellationToken);
                            if (rubricFromBlob != null)
                            {
                                // Use frozen rubric from blob (canonical source for deterministic evaluation)
                                if (!string.IsNullOrEmpty(rubricFromBlob.RubricText))
                                {
                                    question.Rubric = rubricFromBlob.RubricText;
                                }
                                
                                // Use model answer from blob if available
                                if (!string.IsNullOrEmpty(rubricFromBlob.ModelAnswer))
                                {
                                    question.ModelAnswer = rubricFromBlob.ModelAnswer;
                                }
                                
                                // Use keywords from blob
                                if (rubricFromBlob.Keywords.Any())
                                {
                                    question.Keywords = rubricFromBlob.Keywords;
                                }
                                
                                // Override max score with value from SubjectiveRubrics (authoritative)
                                question.MaxScore = rubricInfo.TotalMarks;
                                
                                _logger.LogDebug(
                                    "[FROZEN_RUBRIC] Loaded from SubjectiveRubrics for Q{QuestionId}, TotalMarks={TotalMarks}", 
                                    question.QuestionId, rubricInfo.TotalMarks);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, 
                                "[FROZEN_RUBRIC_FAILED] Could not load blob for Q{QuestionId}, BlobPath={BlobPath}", 
                                question.QuestionId, rubricInfo.BlobPath);
                        }
                    }
                }
            }
            
            return questions;
        }
        
        /// <summary>
        /// Get rubric mappings from SubjectiveRubrics table.
        /// Returns: Dictionary of QuestionId -> (TotalMarks, BlobPath)
        /// </summary>
        private async Task<Dictionary<string, (int TotalMarks, string? BlobPath)>> GetSubjectiveRubricMappingsAsync(
            string examId,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT QuestionId, TotalMarks, RubricBlobPath
                FROM SubjectiveRubrics
                WHERE ExamId = @ExamId";

            var mappings = new Dictionary<string, (int TotalMarks, string? BlobPath)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = new SqlCommand(sql, connection);
                command.CommandTimeout = 30;
                command.Parameters.AddWithValue("@ExamId", examId);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var questionId = reader.GetString(0);
                    var totalMarks = reader.GetInt32(1);
                    var blobPath = reader.IsDBNull(2) ? null : reader.GetString(2);
                    
                    mappings[questionId] = (totalMarks, blobPath);
                }

                _logger.LogInformation(
                    "[SUBJECTIVE_RUBRICS] Found {Count} rubric mappings for exam {ExamId}", 
                    mappings.Count, examId);
            }
            catch (Exception ex)
            {
                // SubjectiveRubrics table may not exist - this is expected during transition
                _logger.LogDebug(ex, "[SUBJECTIVE_RUBRICS] Table query failed (may not exist) for exam {ExamId}", examId);
            }

            return mappings;
        }

        /// <summary>
        /// Get questions from V2 tables (GeneratedExamPapers + GeneratedExamQuestions) with rubrics from blob storage
        /// </summary>
        private async Task<List<ExamQuestionWithRubric>> GetQuestionsFromV2TablesAsync(
            string examId,
            CancellationToken cancellationToken)
        {
            // First check if paper exists with this ExamId
            const string sql = @"
                SELECT q.QuestionId, q.QuestionNumber, q.QuestionText, q.ModelAnswer,
                       q.TotalMarks, q.RubricBlobPath, q.Keywords, q.Topic,
                       q.QuestionType, q.McqOptions, q.CorrectOption,
                       p.Subject, p.Grade, p.Chapter, p.PaperId
                FROM GeneratedExamQuestions q
                INNER JOIN GeneratedExamPapers p ON q.PaperId = p.PaperId
                WHERE p.ExamId = @ExamId AND q.IsActive = 1 AND p.IsActive = 1
                ORDER BY q.QuestionNumber";

            var questions = new List<ExamQuestionWithRubric>();

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                await using var command = new SqlCommand(sql, connection);
                command.CommandTimeout = 30;
                command.Parameters.AddWithValue("@ExamId", examId);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                while (await reader.ReadAsync(cancellationToken))
                {
                    var questionId = reader.GetString(0);
                    var questionNumber = reader.GetInt32(1);
                    var questionText = reader.GetString(2);
                    var modelAnswer = reader.IsDBNull(3) ? "" : reader.GetString(3);
                    var totalMarks = reader.GetInt32(4);
                    var rubricBlobPath = reader.IsDBNull(5) ? null : reader.GetString(5);
                    var keywordsJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                    var topic = reader.IsDBNull(7) ? null : reader.GetString(7);
                    var questionType = reader.IsDBNull(8) ? "subjective" : reader.GetString(8);
                    var mcqOptionsJson = reader.IsDBNull(9) ? null : reader.GetString(9);
                    var correctOption = reader.IsDBNull(10) ? null : reader.GetString(10);
                    var subject = reader.IsDBNull(11) ? null : reader.GetString(11);
                    var grade = reader.IsDBNull(12) ? null : reader.GetString(12);
                    var chapter = reader.IsDBNull(13) ? null : reader.GetString(13);

                    // Parse keywords
                    var keywords = new List<string>();
                    if (!string.IsNullOrEmpty(keywordsJson))
                    {
                        try { keywords = System.Text.Json.JsonSerializer.Deserialize<List<string>>(keywordsJson) ?? new(); } catch { }
                    }

                    // Parse MCQ options
                    var mcqOptions = new List<string>();
                    if (!string.IsNullOrEmpty(mcqOptionsJson))
                    {
                        try { mcqOptions = System.Text.Json.JsonSerializer.Deserialize<List<string>>(mcqOptionsJson) ?? new(); } catch { }
                    }

                    var isMcq = questionType.Equals("mcq", StringComparison.OrdinalIgnoreCase) || mcqOptions.Count > 0;

                    // Build rubric - try to load from blob if available
                    var rubric = $"Topic: {topic ?? chapter ?? subject ?? "General"}. Evaluate based on correct answer and understanding of concepts.";
                    
                    if (!string.IsNullOrEmpty(rubricBlobPath) && _rubricBlobService != null)
                    {
                        try
                        {
                            var rubricFromBlob = await _rubricBlobService.GetRubricAsync(rubricBlobPath, cancellationToken);
                            if (rubricFromBlob != null && !string.IsNullOrEmpty(rubricFromBlob.RubricText))
                            {
                                rubric = rubricFromBlob.RubricText;
                                
                                // Also use keywords from rubric if not in SQL
                                if (rubricFromBlob.Keywords.Any() && !keywords.Any())
                                {
                                    keywords = rubricFromBlob.Keywords;
                                }
                                
                                _logger.LogDebug("Loaded rubric from blob for Q{QuestionNumber}", questionNumber);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load rubric from blob for Q{QuestionNumber}", questionNumber);
                        }
                    }

                    questions.Add(new ExamQuestionWithRubric
                    {
                        QuestionId = questionId,
                        QuestionNumber = questionNumber,
                        QuestionText = questionText,
                        ModelAnswer = modelAnswer,
                        MaxScore = totalMarks,
                        Rubric = rubric,
                        Keywords = keywords,
                        ClassName = grade,
                        Subject = subject,
                        Chapter = chapter ?? topic,
                        IsMcq = isMcq,
                        McqOptions = mcqOptions
                    });
                }
            }
            catch (Exception ex)
            {
                // V2 tables may not exist yet - this is expected during transition
                _logger.LogDebug(ex, "V2 tables query failed (may not exist yet) for exam {ExamId}", examId);
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
                       q.ClassName, q.Subject, q.Chapter, q.QuestionType
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
                
                var questionType = reader.IsDBNull(10) ? "" : reader.GetString(10);
                var isMcq = questionType.Equals("MCQ", StringComparison.OrdinalIgnoreCase) || 
                           questionType.Equals("multiple-choice", StringComparison.OrdinalIgnoreCase) ||
                           questionType.Equals("multiple_choice", StringComparison.OrdinalIgnoreCase);
                
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
                    Chapter = reader.IsDBNull(9) ? null : reader.GetString(9),
                    IsMcq = isMcq
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
                            var partQuestionType = part.TryGetProperty("questionType", out var pqt) ? pqt.GetString() ?? "" : "";
                            
                            if (part.TryGetProperty("questions", out var questionsElement))
                            {
                                foreach (var q in questionsElement.EnumerateArray())
                                {
                                    var question = await ParseQuestionWithBlobRubricAsync(
                                        q, examId, subject, grade, chapter, marksPerQuestion, partQuestionType, cancellationToken);
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
                            var question = await ParseQuestionWithBlobRubricAsync(
                                q, examId, subject, grade, chapter, defaultMarks: 5, partQuestionType: "", cancellationToken);
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
        
        /// <summary>
        /// Parse question from JSON and try to load rubric from blob storage.
        /// Uses path format: paper-{examId}/question-{questionId}.json
        /// </summary>
        private async Task<ExamQuestionWithRubric?> ParseQuestionWithBlobRubricAsync(
            JsonElement q,
            string examId,
            string? subject, 
            string? grade, 
            string? chapter,
            int defaultMarks,
            string partQuestionType,
            CancellationToken cancellationToken)
        {
            // Parse basic question data
            var question = ParseQuestionFromJson(q, subject, grade, chapter, defaultMarks, partQuestionType);
            if (question == null) return null;
            
            // Try to load rubric from blob storage if available
            if (_rubricBlobService != null && !string.IsNullOrEmpty(examId) && !string.IsNullOrEmpty(question.QuestionId))
            {
                try
                {
                    var rubricFromBlob = await _rubricBlobService.GetRubricByIdAsync(
                        examId, question.QuestionId, cancellationToken);
                    
                    if (rubricFromBlob != null)
                    {
                        // Use rubric text from blob (normalized with step-wise marking)
                        if (!string.IsNullOrEmpty(rubricFromBlob.RubricText))
                        {
                            question.Rubric = rubricFromBlob.RubricText;
                            _logger.LogDebug("Loaded rubric from blob for Q{QuestionNumber} ({ExamId}/{QuestionId})", 
                                question.QuestionNumber, examId, question.QuestionId);
                        }
                        
                        // Use model answer from blob if current one is empty
                        if (string.IsNullOrEmpty(question.ModelAnswer) && !string.IsNullOrEmpty(rubricFromBlob.ModelAnswer))
                        {
                            question.ModelAnswer = rubricFromBlob.ModelAnswer;
                        }
                        
                        // Use keywords from blob if current ones are empty
                        if (!question.Keywords.Any() && rubricFromBlob.Keywords.Any())
                        {
                            question.Keywords = rubricFromBlob.Keywords;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to load rubric from blob for Q{QuestionNumber} - using inline rubric", 
                        question.QuestionNumber);
                }
            }
            
            return question;
        }

        private ExamQuestionWithRubric? ParseQuestionFromJson(
            JsonElement q, 
            string? subject, 
            string? grade, 
            string? chapter,
            int defaultMarks,
            string partQuestionType = "")
        {
            try
            {
                // Support multiple field name variations
                var questionId = q.TryGetProperty("questionId", out var qid) ? qid.GetString() ?? "" 
                    : q.TryGetProperty("id", out var id) ? id.GetString() ?? "" 
                    : Guid.NewGuid().ToString();
                
                var questionNumber = q.TryGetProperty("questionNumber", out var qn) ? qn.GetInt32() 
                    : q.TryGetProperty("number", out var num) ? num.GetInt32() 
                    : 0;
                
                var questionText = q.TryGetProperty("questionText", out var qt) ? qt.GetString() ?? "" 
                    : q.TryGetProperty("question", out var que) ? que.GetString() ?? "" 
                    : "";
                
                // Support multiple field names for answers: correctAnswer, modelAnswer, answer, idealAnswer
                var correctAnswer = q.TryGetProperty("correctAnswer", out var ca) ? ca.GetString() ?? "" 
                    : q.TryGetProperty("modelAnswer", out var ma) ? ma.GetString() ?? ""
                    : q.TryGetProperty("answer", out var ans) ? ans.GetString() ?? ""
                    : q.TryGetProperty("idealAnswer", out var ia) ? ia.GetString() ?? ""
                    : "";
                
                var topic = q.TryGetProperty("topic", out var t) ? t.GetString() ?? "" 
                    : q.TryGetProperty("chapter", out var ch) ? ch.GetString() ?? ""
                    : "";
                
                var marks = q.TryGetProperty("marks", out var m) ? m.GetInt32() 
                    : q.TryGetProperty("maxScore", out var ms) ? ms.GetInt32()
                    : q.TryGetProperty("points", out var pts) ? pts.GetInt32()
                    : defaultMarks;

                // Determine if question is MCQ from part-level questionType or question-level type
                var questionType = q.TryGetProperty("questionType", out var qt1) ? qt1.GetString() ?? "" 
                    : q.TryGetProperty("type", out var qt2) ? qt2.GetString() ?? ""
                    : partQuestionType;
                
                bool isMcq = questionType.Equals("MCQ", StringComparison.OrdinalIgnoreCase) || 
                            questionType.Equals("multiple-choice", StringComparison.OrdinalIgnoreCase) ||
                            questionType.Equals("multiple_choice", StringComparison.OrdinalIgnoreCase);
                
                // Try to get MCQ options array
                var mcqOptions = new List<string>();
                if (q.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var option in opts.EnumerateArray())
                    {
                        var optionText = option.GetString();
                        if (!string.IsNullOrWhiteSpace(optionText))
                        {
                            mcqOptions.Add(optionText);
                        }
                    }
                    // If we have options array, it's definitely an MCQ
                    if (mcqOptions.Count > 0)
                    {
                        isMcq = true;
                    }
                }

                // Try to get rubric if available in JSON
                var rubricFromJson = q.TryGetProperty("rubric", out var rub) ? rub.GetString() ?? "" : "";
                
                // Try to get keywords array if available
                var keywordsFromJson = new List<string>();
                if (q.TryGetProperty("keywords", out var kw) && kw.ValueKind == JsonValueKind.Array)
                {
                    foreach (var keyword in kw.EnumerateArray())
                    {
                        var keywordText = keyword.GetString();
                        if (!string.IsNullOrWhiteSpace(keywordText))
                        {
                            keywordsFromJson.Add(keywordText);
                        }
                    }
                }

                // Skip if no question text
                if (string.IsNullOrWhiteSpace(questionText))
                {
                    _logger.LogWarning("Skipping question with empty text. QuestionId: {QuestionId}, QuestionNumber: {QuestionNumber}", 
                        questionId, questionNumber);
                    return null;
                }

                // Build rubric - use JSON rubric if available, otherwise generate from topic
                var rubric = !string.IsNullOrWhiteSpace(rubricFromJson) 
                    ? rubricFromJson
                    : !string.IsNullOrEmpty(topic) 
                        ? $"Topic: {topic}. Evaluate based on correct answer and understanding of concepts. Award partial credit for partially correct answers."
                        : "Evaluate based on correct answer and understanding of concepts. Award partial credit for partially correct answers.";

                // Extract keywords - use JSON keywords if available, otherwise extract from topic/answer
                var keywords = keywordsFromJson.Any() ? keywordsFromJson : new List<string>();
                if (!keywords.Any() && !string.IsNullOrEmpty(topic))
                {
                    keywords.Add(topic);
                }

                var result = new ExamQuestionWithRubric
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
                    Chapter = chapter ?? topic,
                    IsMcq = isMcq,
                    McqOptions = mcqOptions
                };

                _logger.LogDebug("Parsed question from GeneratedExams: Q{QuestionNumber}, IsMcq: {IsMcq}, ModelAnswer length: {Length}, MaxScore: {MaxScore}", 
                    questionNumber, isMcq, correctAnswer.Length, marks);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse question from JSON. Question data: {JsonData}", 
                    q.GetRawText().Substring(0, Math.Min(200, q.GetRawText().Length)));
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
