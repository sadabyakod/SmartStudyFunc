using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Linq;

namespace SmartStudyFunc.Functions
{
    public class GenerateModelExam
    {
        private readonly ILogger<GenerateModelExam> _logger;
        private readonly string _connectionString;

        public GenerateModelExam(ILogger<GenerateModelExam> logger, IConfiguration config)
        {
            _logger = logger;
            _connectionString = config["ConnectionStrings:SqlDb"]!;
        }

        [Function("GenerateModelExam")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "exam/generate")] HttpRequestData req)
        {
            using var con = new SqlConnection(_connectionString);
            _logger.LogInformation("Generating Model Exam...");

            // 1) Create new exam
            int examId = await con.ExecuteScalarAsync<int>(@"
                INSERT INTO GeneratedExams DEFAULT VALUES;
                SELECT SCOPE_IDENTITY();
            ");

            // 2) Random question selection
            var partA = (await con.QueryAsync("SELECT TOP 10 * FROM GeneratedQuestions WHERE Marks = 1 ORDER BY NEWID()")).ToList();
            var partB = (await con.QueryAsync("SELECT TOP 10 * FROM GeneratedQuestions WHERE Marks = 2 ORDER BY NEWID()")).ToList();
            var partC = (await con.QueryAsync("SELECT TOP 6 * FROM GeneratedQuestions WHERE Marks = 3 ORDER BY NEWID()")).ToList();
            var partD = (await con.QueryAsync("SELECT TOP 4 * FROM GeneratedQuestions WHERE Marks = 5 ORDER BY NEWID()")).ToList();

            // 3) Insert into GeneratedExamQuestions
            foreach (var q in partA) await InsertExamQuestion(con, examId, (int)q.Id, 1, "A");
            foreach (var q in partB) await InsertExamQuestion(con, examId, (int)q.Id, 2, "B");
            foreach (var q in partC) await InsertExamQuestion(con, examId, (int)q.Id, 3, "C");
            foreach (var q in partD) await InsertExamQuestion(con, examId, (int)q.Id, 5, "D");

            // 4) Build JSON result
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);

            await response.WriteAsJsonAsync(new
            {
                examId,
                partA = partA.Select(x => new { x.Id, x.QuestionText }),
                partB = partB.Select(x => new { x.Id, x.QuestionText }),
                partC = partC.Select(x => new { x.Id, x.QuestionText }),
                partD = partD.Select(x => new { x.Id, x.QuestionText })
            });

            _logger.LogInformation("Model exam generated successfully");

            return response;
        }

        private async Task InsertExamQuestion(SqlConnection con, int examId, int questionId, int marks, string section)
        {
            await con.ExecuteAsync(@"
                INSERT INTO GeneratedExamQuestions (ExamId, QuestionId, Marks, Section)
                VALUES (@ExamId, @QuestionId, @Marks, @Section)
            ", new
            {
                ExamId = examId,
                QuestionId = questionId,
                Marks = marks,
                Section = section
            });
        }
    }
}
