using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// Enhanced exam generation with rubric storage in Azure Blob.
    /// Stores questions in SQL and detailed rubrics in modalquestions-rubrics container.
    /// </summary>
    public class GenerateModelExamV2
    {
        private readonly ILogger<GenerateModelExamV2> _logger;
        private readonly string _connectionString;
        private readonly IRubricBlobService _rubricBlobService;

        public GenerateModelExamV2(
            ILogger<GenerateModelExamV2> logger, 
            IConfiguration config,
            IRubricBlobService rubricBlobService)
        {
            _logger = logger;
            _connectionString = config["SqlConnectionString"] 
                ?? config.GetConnectionString("SqlDb") 
                ?? config["ConnectionStrings:SqlDb"]!;
            _rubricBlobService = rubricBlobService;
        }

        /// <summary>
        /// Generate a model exam paper with questions and rubrics stored in SQL + Blob
        /// POST /api/exam/generate/v2
        /// </summary>
        [Function("GenerateModelExamV2")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "exam/generate/v2")] HttpRequestData req)
        {
            var cancellationToken = req.FunctionContext.CancellationToken;
            
            try
            {
                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var request = string.IsNullOrEmpty(requestBody) 
                    ? new GenerateExamRequest() 
                    : JsonSerializer.Deserialize<GenerateExamRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? new GenerateExamRequest();

                _logger.LogInformation(
                    "[GENERATE_EXAM_V2] Subject={Subject}, Grade={Grade}, Chapter={Chapter}",
                    request.Subject, request.Grade, request.Chapter);

                // Generate paper ID
                var paperId = $"PAPER-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";
                var examId = request.ExamId ?? $"EXAM-{DateTime.UtcNow:yyyyMMddHHmmss}";

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);

                // Get questions from GeneratedQuestions table based on parts config
                var parts = request.Parts ?? GetDefaultParts();
                var allQuestions = new List<(GeneratedExamQuestion Question, QuestionRubric Rubric)>();
                var questionNumber = 1;

                foreach (var (section, config) in parts)
                {
                    var questions = await GetQuestionsForSection(
                        connection, section, config, request.Subject, request.Grade, 
                        request.Chapter, request.Seed, questionNumber, cancellationToken);
                    
                    allQuestions.AddRange(questions);
                    questionNumber += questions.Count;
                }

                if (allQuestions.Count == 0)
                {
                    _logger.LogWarning("[GENERATE_EXAM_V2] No questions found for the given criteria");
                    var errorResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await errorResponse.WriteAsJsonAsync(new { error = "No questions found for the given criteria" });
                    return errorResponse;
                }

                // Save paper to SQL
                var paper = new GeneratedExamPaper
                {
                    PaperId = paperId,
                    ExamId = examId,
                    Seed = request.Seed,
                    Subject = request.Subject,
                    Grade = request.Grade,
                    Chapter = request.Chapter,
                    TotalMarks = allQuestions.Sum(q => q.Question.TotalMarks),
                    TotalQuestions = allQuestions.Count
                };

                await SavePaperToSqlAsync(connection, paper, cancellationToken);

                // Save questions to SQL and rubrics to Blob
                var rubrics = new List<QuestionRubric>();
                foreach (var (question, rubric) in allQuestions)
                {
                    question.PaperId = paperId;
                    rubric.PaperId = paperId;
                    rubrics.Add(rubric);
                    
                    // Save to blob first to get the path
                    var blobPath = await _rubricBlobService.SaveRubricAsync(rubric, cancellationToken);
                    question.RubricBlobPath = blobPath;
                    
                    // Save question to SQL
                    await SaveQuestionToSqlAsync(connection, question, cancellationToken);
                }

                _logger.LogInformation(
                    "[EXAM_GENERATED] PaperId={PaperId}, ExamId={ExamId}, TotalQuestions={Count}, TotalMarks={Marks}",
                    paperId, examId, allQuestions.Count, paper.TotalMarks);

                // Build response
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    paperId,
                    examId,
                    subject = request.Subject,
                    grade = request.Grade,
                    chapter = request.Chapter,
                    totalQuestions = allQuestions.Count,
                    totalMarks = paper.TotalMarks,
                    parts = parts.Select(p => new
                    {
                        section = p.Key,
                        count = p.Value.Count,
                        marksPerQuestion = p.Value.Marks,
                        type = p.Value.Type
                    }),
                    questions = allQuestions.Select(q => new
                    {
                        questionId = q.Question.QuestionId,
                        questionNumber = q.Question.QuestionNumber,
                        section = q.Question.Section,
                        questionText = q.Question.QuestionText,
                        type = q.Question.QuestionType,
                        marks = q.Question.TotalMarks,
                        rubricBlobPath = q.Question.RubricBlobPath,
                        mcqOptions = q.Question.McqOptions.Count > 0 ? q.Question.McqOptions : null
                    })
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GENERATE_EXAM_V2_ERROR] Failed to generate exam");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
                return errorResponse;
            }
        }

        private Dictionary<string, PartConfig> GetDefaultParts()
        {
            return new Dictionary<string, PartConfig>
            {
                ["A"] = new PartConfig { Count = 5, Marks = 1, Type = "mcq" },
                ["B"] = new PartConfig { Count = 5, Marks = 2, Type = "subjective" },
                ["C"] = new PartConfig { Count = 3, Marks = 3, Type = "subjective" },
                ["D"] = new PartConfig { Count = 2, Marks = 5, Type = "subjective" }
            };
        }

        private async Task<List<(GeneratedExamQuestion Question, QuestionRubric Rubric)>> GetQuestionsForSection(
            SqlConnection connection,
            string section,
            PartConfig config,
            string? subject,
            string? grade,
            string? chapter,
            int? seed,
            int startQuestionNumber,
            CancellationToken cancellationToken)
        {
            var result = new List<(GeneratedExamQuestion, QuestionRubric)>();

            // Build query to get questions from GeneratedQuestions table
            var sql = @"
                SELECT TOP (@Count) 
                    Id, QuestionText, Answer, Marks, Topic, Keywords
                FROM GeneratedQuestions 
                WHERE Marks = @Marks
                ORDER BY NEWID()";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Count", config.Count);
            command.Parameters.AddWithValue("@Marks", config.Marks);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            
            var questionNum = startQuestionNumber;
            while (await reader.ReadAsync(cancellationToken))
            {
                var questionId = $"{section}{questionNum - startQuestionNumber + 1}";
                var questionText = reader.GetString(1);
                var modelAnswer = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var marks = reader.GetInt32(3);
                var topic = reader.IsDBNull(4) ? null : reader.GetString(4);
                var keywordsJson = reader.IsDBNull(5) ? null : reader.GetString(5);

                var keywords = new List<string>();
                if (!string.IsNullOrEmpty(keywordsJson))
                {
                    try
                    {
                        keywords = JsonSerializer.Deserialize<List<string>>(keywordsJson) ?? new List<string>();
                    }
                    catch { }
                }

                var isMcq = config.Type.Equals("mcq", StringComparison.OrdinalIgnoreCase);

                var question = new GeneratedExamQuestion
                {
                    QuestionId = questionId,
                    QuestionNumber = questionNum,
                    Section = section,
                    QuestionText = questionText,
                    QuestionType = config.Type,
                    TotalMarks = marks,
                    ModelAnswer = modelAnswer,
                    Keywords = keywords,
                    Topic = topic
                };

                // Create detailed rubric
                var rubric = new QuestionRubric
                {
                    QuestionId = questionId,
                    QuestionNumber = questionNum,
                    QuestionText = questionText,
                    QuestionType = config.Type,
                    TotalMarks = marks,
                    ModelAnswer = modelAnswer,
                    Topic = topic,
                    Subject = subject,
                    Grade = grade,
                    Chapter = chapter,
                    Keywords = keywords,
                    RubricText = GenerateRubricText(questionText, modelAnswer, marks, topic),
                    MarkingSteps = GenerateMarkingSteps(questionText, modelAnswer, marks, isMcq)
                };

                result.Add((question, rubric));
                questionNum++;
            }

            return result;
        }

        private string GenerateRubricText(string questionText, string modelAnswer, int marks, string? topic)
        {
            var rubric = $"Topic: {topic ?? "General"}. ";
            rubric += $"Total Marks: {marks}. ";
            rubric += "Evaluate based on correct answer and understanding of concepts. ";
            rubric += "Award partial credit for partially correct answers. ";
            rubric += "Check for key steps, formula usage, and final answer correctness.";
            return rubric;
        }

        private List<RubricMarkingStep> GenerateMarkingSteps(string questionText, string modelAnswer, int marks, bool isMcq)
        {
            var steps = new List<RubricMarkingStep>();

            if (isMcq)
            {
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 1,
                    Description = "Correct option selected",
                    MaxMarks = marks,
                    Keywords = new List<string>(),
                    Criteria = "Full marks for correct option, zero for incorrect"
                });
            }
            else if (marks == 1)
            {
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 1,
                    Description = "Correct answer",
                    MaxMarks = 1,
                    Keywords = new List<string>(),
                    Criteria = "Award 1 mark for correct final answer"
                });
            }
            else if (marks == 2)
            {
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 1,
                    Description = "Correct method/formula",
                    MaxMarks = 1,
                    Keywords = new List<string> { "formula", "method" },
                    Criteria = "Award 1 mark for correct approach"
                });
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 2,
                    Description = "Correct final answer",
                    MaxMarks = 1,
                    Keywords = new List<string> { "answer", "result" },
                    Criteria = "Award 1 mark for correct final answer"
                });
            }
            else if (marks == 3)
            {
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 1,
                    Description = "Correct formula/method identification",
                    MaxMarks = 1,
                    Keywords = new List<string> { "formula", "method" },
                    Criteria = "Award 1 mark for identifying correct approach"
                });
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 2,
                    Description = "Correct substitution/application",
                    MaxMarks = 1,
                    Keywords = new List<string> { "substitution", "calculation" },
                    Criteria = "Award 1 mark for correct application"
                });
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 3,
                    Description = "Correct final answer with units",
                    MaxMarks = 1,
                    Keywords = new List<string> { "answer", "units" },
                    Criteria = "Award 1 mark for correct final answer"
                });
            }
            else // 5 marks or more
            {
                var marksPerStep = marks / 4.0m;
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 1,
                    Description = "Problem understanding and setup",
                    MaxMarks = Math.Round(marksPerStep, 1),
                    Keywords = new List<string> { "diagram", "given", "find" },
                    Criteria = "Award marks for correct problem interpretation"
                });
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 2,
                    Description = "Correct formula/theorem application",
                    MaxMarks = Math.Round(marksPerStep, 1),
                    Keywords = new List<string> { "formula", "theorem" },
                    Criteria = "Award marks for using correct formulas"
                });
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 3,
                    Description = "Step-by-step calculation",
                    MaxMarks = Math.Round(marksPerStep, 1),
                    Keywords = new List<string> { "calculation", "steps" },
                    Criteria = "Award marks for showing work"
                });
                steps.Add(new RubricMarkingStep
                {
                    StepNumber = 4,
                    Description = "Correct final answer with proper presentation",
                    MaxMarks = marks - (3 * Math.Round(marksPerStep, 1)),
                    Keywords = new List<string> { "answer", "conclusion" },
                    Criteria = "Award remaining marks for final answer"
                });
            }

            return steps;
        }

        private async Task SavePaperToSqlAsync(
            SqlConnection connection, 
            GeneratedExamPaper paper,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO GeneratedExamPapers 
                    (Id, PaperId, ExamId, Seed, Version, Subject, Grade, Chapter, TotalMarks, TotalQuestions, CreatedAt, IsActive)
                VALUES 
                    (@Id, @PaperId, @ExamId, @Seed, @Version, @Subject, @Grade, @Chapter, @TotalMarks, @TotalQuestions, @CreatedAt, 1)";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", paper.Id);
            command.Parameters.AddWithValue("@PaperId", paper.PaperId);
            command.Parameters.AddWithValue("@ExamId", paper.ExamId);
            command.Parameters.AddWithValue("@Seed", (object?)paper.Seed ?? DBNull.Value);
            command.Parameters.AddWithValue("@Version", paper.Version);
            command.Parameters.AddWithValue("@Subject", (object?)paper.Subject ?? DBNull.Value);
            command.Parameters.AddWithValue("@Grade", (object?)paper.Grade ?? DBNull.Value);
            command.Parameters.AddWithValue("@Chapter", (object?)paper.Chapter ?? DBNull.Value);
            command.Parameters.AddWithValue("@TotalMarks", paper.TotalMarks);
            command.Parameters.AddWithValue("@TotalQuestions", paper.TotalQuestions);
            command.Parameters.AddWithValue("@CreatedAt", paper.CreatedAt);

            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("[PAPER_SAVED] PaperId={PaperId}, ExamId={ExamId}", paper.PaperId, paper.ExamId);
        }

        private async Task SaveQuestionToSqlAsync(
            SqlConnection connection,
            GeneratedExamQuestion question,
            CancellationToken cancellationToken)
        {
            const string sql = @"
                INSERT INTO GeneratedExamQuestions 
                    (Id, PaperId, QuestionId, QuestionNumber, Section, QuestionText, QuestionType, 
                     TotalMarks, ModelAnswer, RubricBlobPath, Keywords, Topic, McqOptions, CorrectOption, CreatedAt, IsActive)
                VALUES 
                    (@Id, @PaperId, @QuestionId, @QuestionNumber, @Section, @QuestionText, @QuestionType,
                     @TotalMarks, @ModelAnswer, @RubricBlobPath, @Keywords, @Topic, @McqOptions, @CorrectOption, @CreatedAt, 1)";

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", question.Id);
            command.Parameters.AddWithValue("@PaperId", question.PaperId);
            command.Parameters.AddWithValue("@QuestionId", question.QuestionId);
            command.Parameters.AddWithValue("@QuestionNumber", question.QuestionNumber);
            command.Parameters.AddWithValue("@Section", (object?)question.Section ?? DBNull.Value);
            command.Parameters.AddWithValue("@QuestionText", question.QuestionText);
            command.Parameters.AddWithValue("@QuestionType", question.QuestionType);
            command.Parameters.AddWithValue("@TotalMarks", question.TotalMarks);
            command.Parameters.AddWithValue("@ModelAnswer", (object?)question.ModelAnswer ?? DBNull.Value);
            command.Parameters.AddWithValue("@RubricBlobPath", (object?)question.RubricBlobPath ?? DBNull.Value);
            command.Parameters.AddWithValue("@Keywords", question.Keywords.Any() ? JsonSerializer.Serialize(question.Keywords) : DBNull.Value);
            command.Parameters.AddWithValue("@Topic", (object?)question.Topic ?? DBNull.Value);
            command.Parameters.AddWithValue("@McqOptions", question.McqOptions.Any() ? JsonSerializer.Serialize(question.McqOptions) : DBNull.Value);
            command.Parameters.AddWithValue("@CorrectOption", (object?)question.CorrectOption ?? DBNull.Value);
            command.Parameters.AddWithValue("@CreatedAt", question.CreatedAt);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
