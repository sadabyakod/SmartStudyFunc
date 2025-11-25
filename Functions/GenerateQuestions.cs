using Azure;
using Azure.AI.OpenAI;
using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    public class GenerateQuestions
    {
        private readonly ILogger<GenerateQuestions> _logger;
        private readonly string _connectionString;
        private readonly OpenAIClient _openAi;

        public GenerateQuestions(ILogger<GenerateQuestions> logger, IConfiguration config)
        {
            _logger = logger;
            _connectionString = config["ConnectionStrings:SqlDb"]!;

            _openAi = new OpenAIClient(
                new Uri(Environment.GetEnvironmentVariable("OpenAIEndpoint")!),
                new AzureKeyCredential(Environment.GetEnvironmentVariable("OpenAIKey")!)
            );
        }

        [Function("GenerateQuestions")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "questions/generate")] HttpRequestData req)
        {
            _logger.LogInformation("==== Starting Auto Question Generation ====");

            using var con = new SqlConnection(_connectionString);

            var chapters = await con.QueryAsync("SELECT Id, UnitName, ChapterName FROM Chapters");

            foreach (var chapter in chapters)
            {
                int chapterId = (int)chapter.Id;
                string chapterName = (string)chapter.ChapterName;
                _logger.LogInformation("Generating questions for: " + chapterName);

                await GenerateQuestionsForChapter(con, chapterId, chapterName, 1, 10);
                await GenerateQuestionsForChapter(con, chapter.Id, chapter.ChapterName, 2, 8);
                await GenerateQuestionsForChapter(con, chapter.Id, chapter.ChapterName, 3, 6);
                await GenerateQuestionsForChapter(con, chapter.Id, chapter.ChapterName, 5, 4);
            }

            _logger.LogInformation("==== Question Generation Completed ====");

            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteStringAsync("Questions generated successfully.");
            return response;
        }

        private async Task GenerateQuestionsForChapter(SqlConnection con, int chapterId, string chapterName, int marks, int count)
        {
            string prompt = $@"
Generate {count} ORIGINAL Karnataka PUC Mathematics questions.

Chapter: {chapterName}
Marks per question: {marks}

Return JSON ONLY in this format:
[
  {{
    ""question"": ""..."",
    ""idealAnswer"": ""..."",
    ""keywords"": [""..."", ""..."", ""...""]
  }}
]";

            var chatOptions = new ChatCompletionsOptions
            {
                DeploymentName = "gpt-4o-mini",
                Messages =
                {
                    new ChatRequestSystemMessage("You are a PUC Maths examiner."),
                    new ChatRequestUserMessage(prompt)
                }
            };

            var result = await _openAi.GetChatCompletionsAsync(chatOptions);

            var json = result.Value.Choices[0].Message.Content;

            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement;

            foreach (var item in items.EnumerateArray())
            {
                var keywords = new System.Collections.Generic.List<string>();
                if (item.TryGetProperty("keywords", out var keywordsElement))
                {
                    foreach (var kw in keywordsElement.EnumerateArray())
                    {
                        keywords.Add(kw.GetString()!);
                    }
                }

                await con.ExecuteAsync(@"
                    INSERT INTO GeneratedQuestions
                    (ChapterId, Marks, QuestionText, IdealAnswer, Keywords)
                    VALUES
                    (@ChapterId, @Marks, @Question, @Answer, @Keywords)
                ", new
                {
                    ChapterId = chapterId,
                    Marks = marks,
                    Question = item.GetProperty("question").GetString()!,
                    Answer = item.GetProperty("idealAnswer").GetString()!,
                    Keywords = string.Join(",", keywords)
                });
            }
        }
    }
}
