using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Service for evaluating written answers using Azure OpenAI with
    /// STATE BOARD BLUEPRINT STYLE step-wise partial credit marking.
    /// </summary>
    public interface IWrittenAnswerEvaluationService
    {
        Task<WrittenEvaluationResult> EvaluateSubmissionAsync(
            WrittenSubmission submission,
            string extractedText,
            List<ExamQuestionWithRubric> questions,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Board-compliant step-wise answer evaluation service.
    /// 
    /// EVALUATION FLOW:
    /// 1. Fetch syllabus-aligned EXPECTED ANSWER using RAG
    /// 2. Generate STEP-WISE MARKING SCHEME (Board Blueprint)
    /// 3. Evaluate student answer AGAINST EACH STEP
    /// 4. Award PARTIAL CREDIT per step (State Board style)
    /// 5. Generate structured JSON output
    /// </summary>
    public class WrittenAnswerEvaluationService : IWrittenAnswerEvaluationService
    {
        private readonly OpenAIClient _openAiClient;
        private readonly string _deploymentName;
        private readonly ISyllabusRagService _syllabusRagService;
        private readonly ILogger<WrittenAnswerEvaluationService> _logger;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(120);

        // JSON serializer options for consistent output
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public WrittenAnswerEvaluationService(
            OpenAIClient openAiClient,
            string deploymentName,
            ISyllabusRagService syllabusRagService,
            ILogger<WrittenAnswerEvaluationService> logger)
        {
            _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
            _deploymentName = deploymentName ?? throw new ArgumentNullException(nameof(deploymentName));
            _syllabusRagService = syllabusRagService ?? throw new ArgumentNullException(nameof(syllabusRagService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Evaluates a complete written submission using board blueprint style marking.
        /// </summary>
        public async Task<WrittenEvaluationResult> EvaluateSubmissionAsync(
            WrittenSubmission submission,
            string extractedText,
            List<ExamQuestionWithRubric> questions,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[{SubmissionId}] Starting BOARD BLUEPRINT evaluation for {QuestionCount} questions",
                submission.Id, questions.Count);

            var result = new WrittenEvaluationResult
            {
                WrittenSubmissionId = submission.Id,
                ExamId = submission.ExamId,
                StudentId = submission.StudentId,
                EvaluatedAt = DateTime.UtcNow
            };

            // Step 1: Segment OCR text into per-question answers
            var segmentedAnswers = await SegmentAnswersByQuestionAsync(
                extractedText, questions, submission.Id, cancellationToken);

            // Step 2: Evaluate each question with step-wise marking
            var evaluations = new List<WrittenQuestionEvaluation>();
            decimal totalScore = 0;
            decimal maxPossibleScore = 0;

            foreach (var question in questions.OrderBy(q => q.QuestionNumber))
            {
                var studentAnswer = segmentedAnswers.GetValueOrDefault(
                    question.QuestionNumber,
                    "[Answer not found in submission]");
                    
                try
                {
                    var evaluation = await EvaluateSingleQuestionStepWiseAsync(
                        submission.Id,
                        question,
                        studentAnswer,
                        cancellationToken);

                    evaluations.Add(evaluation);
                    totalScore += evaluation.AwardedScore;
                    maxPossibleScore += question.MaxScore;

                    _logger.LogInformation(
                        "[{SubmissionId}] Q{QuestionNumber} step-wise evaluation: {Score}/{MaxScore}",
                        submission.Id, question.QuestionNumber,
                        evaluation.AwardedScore, question.MaxScore);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[{SubmissionId}] Failed to evaluate Q{QuestionNumber}. Awarding 0 marks.",
                        submission.Id, question.QuestionNumber);

                    // Don't throw - continue with 0 marks for this question
                    evaluations.Add(new WrittenQuestionEvaluation
                    {
                        Id = Guid.NewGuid(),
                        WrittenSubmissionId = submission.Id,
                        QuestionId = question.QuestionId,
                        QuestionNumber = question.QuestionNumber,
                        ExtractedAnswer = studentAnswer,
                        MaxScore = question.MaxScore,
                        AwardedScore = 0,
                        Feedback = "Evaluation failed due to system error.",
                        RubricBreakdown = "{}",
                        EvaluatedAt = DateTime.UtcNow
                    });
                    maxPossibleScore += question.MaxScore;
                }
            }

            result.QuestionEvaluations = evaluations;
            result.TotalScore = totalScore;
            result.MaxPossibleScore = maxPossibleScore;
            result.Percentage = maxPossibleScore > 0
                ? Math.Round((totalScore / maxPossibleScore) * 100, 2)
                : 0;
            result.Grade = CalculateGrade(result.Percentage);

            _logger.LogInformation(
                "[{SubmissionId}] BOARD BLUEPRINT evaluation completed: {TotalScore}/{MaxScore} ({Percentage}%) Grade={Grade}",
                submission.Id, totalScore, maxPossibleScore, result.Percentage, result.Grade);

            return result;
        }

        /// <summary>
        /// Evaluates a single question using step-wise board blueprint marking.
        /// 
        /// This method performs a SINGLE OpenAI call that:
        /// 1. Uses syllabus chunks to generate expected answer
        /// 2. Creates step-wise marking scheme
        /// 3. Evaluates student answer against each step
        /// 4. Awards partial credit per step
        /// </summary>
        private async Task<WrittenQuestionEvaluation> EvaluateSingleQuestionStepWiseAsync(
            Guid submissionId,
            ExamQuestionWithRubric question,
            string studentAnswer,
            CancellationToken cancellationToken)
        {
            // Step 1: Fetch relevant syllabus chunks using RAG
            var syllabusChunks = await _syllabusRagService.GetRelevantSyllabusChunksAsync(
                question.QuestionText,
                question.ClassName,
                question.Subject,
                question.Chapter,
                topN: 5,
                cancellationToken);

            var syllabusContext = syllabusChunks.Any()
                ? string.Join("\n\n---\n\n", syllabusChunks.Select(c => c.ChunkText))
                : question.ModelAnswer; // Fallback to model answer if no syllabus

            var syllabusChunkIds = syllabusChunks.Select(c => c.ChunkId).ToList();

            _logger.LogDebug(
                "[{SubmissionId}] Q{QuestionNumber}: Retrieved {ChunkCount} syllabus chunks",
                submissionId, question.QuestionNumber, syllabusChunks.Count);

            // Step 2: Build comprehensive evaluation prompt
            var prompt = BuildStepWiseEvaluationPrompt(
                question,
                studentAnswer,
                syllabusContext);

            // Step 3: Call OpenAI (SINGLE CALL per question)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var options = new ChatCompletionsOptions
            {
                DeploymentName = _deploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage(GetBoardBlueprintSystemPrompt()),
                    new ChatRequestUserMessage(prompt)
                },
                Temperature = 0.1f, // Low temperature for deterministic output
                MaxTokens = 2000
            };

            var response = await _openAiClient.GetChatCompletionsAsync(options, cts.Token);
            var content = response.Value.Choices[0].Message.Content;

            // Step 4: Parse structured response
            var evalResult = ParseStepWiseEvaluationResponse(
                content,
                question.MaxScore,
                syllabusChunkIds);

            return new WrittenQuestionEvaluation
            {
                Id = Guid.NewGuid(),
                WrittenSubmissionId = submissionId,
                QuestionId = question.QuestionId,
                QuestionNumber = question.QuestionNumber,
                ExtractedAnswer = studentAnswer,
                ModelAnswer = evalResult.ExpectedAnswer.Summary,
                MaxScore = question.MaxScore,
                AwardedScore = evalResult.StudentEvaluation.TotalAwardedMarks,
                Feedback = evalResult.OverallFeedback,
                RubricBreakdown = JsonSerializer.Serialize(evalResult, JsonOptions),
                EvaluatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Board Blueprint system prompt for step-wise evaluation.
        /// </summary>
        private static string GetBoardBlueprintSystemPrompt()
        {
            return @"You are an expert STATE BOARD EXAMINER evaluating student answers using BLUEPRINT STYLE MARKING.

YOUR ROLE:
- Generate expected answers STRICTLY from the provided syllabus content
- Create STEP-WISE marking schemes (board blueprint style)
- Award PARTIAL CREDIT per step
- NEVER penalize spelling errors or OCR noise from handwriting
- Be FAIR but RIGOROUS

PARTIAL CREDIT RULES (STATE BOARD STYLE):
1. Correct method but wrong final answer → award method marks
2. Correct formula but wrong substitution → partial marks
3. Diagram description present → credit diagram marks
4. Key concept present even if wording differs → award marks
5. Logical steps attempted → award attempt marks
6. Copied formula from question without any working/explanation → 0 marks for that step
7. Arithmetic/calculation error with correct method → award method marks, deduct only calculation marks

RESPONSE FORMAT (STRICT JSON - NO MARKDOWN):
{
  ""questionNumber"": <int>,
  ""maxMarks"": <decimal>,
  ""expectedAnswer"": {
    ""summary"": ""<correct syllabus-based answer, 2-3 sentences>"",
    ""steps"": [
      {
        ""stepNumber"": 1,
        ""description"": ""<what this step requires>"",
        ""keywords"": [""keyword1"", ""keyword2""],
        ""marks"": <decimal>
      }
    ]
  },
  ""studentEvaluation"": {
    ""steps"": [
      {
        ""stepNumber"": 1,
        ""awardedMarks"": <decimal>,
        ""maxMarks"": <decimal>,
        ""reason"": ""<1-2 line explanation>""
      }
    ],
    ""totalAwardedMarks"": <decimal>,
    ""confidenceScore"": <decimal 0.0-1.0>
  },
  ""overallFeedback"": ""<2-3 sentences suitable for teachers and students>""
}

CRITICAL RULES:
- Sum of step marks MUST equal maxMarks
- Sum of awardedMarks MUST equal totalAwardedMarks
- totalAwardedMarks MUST be between 0 and maxMarks
- Return ONLY valid JSON, no markdown code blocks
- Do NOT hallucinate content outside syllabus";
        }

        /// <summary>
        /// Builds the evaluation prompt with syllabus context and student answer.
        /// </summary>
        private static string BuildStepWiseEvaluationPrompt(
            ExamQuestionWithRubric question,
            string studentAnswer,
            string syllabusContext)
        {
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine("EXAM QUESTION EVALUATION REQUEST");
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine();

            sb.AppendLine($"QUESTION NUMBER: {question.QuestionNumber}");
            sb.AppendLine($"MAX MARKS: {question.MaxScore}");
            sb.AppendLine($"CLASS: {question.ClassName}");
            sb.AppendLine($"SUBJECT: {question.Subject}");
            sb.AppendLine($"CHAPTER: {question.Chapter}");
            sb.AppendLine();

            sb.AppendLine("QUESTION TEXT:");
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine(question.QuestionText);
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine();

            sb.AppendLine("SYLLABUS CONTENT (USE THIS TO GENERATE EXPECTED ANSWER):");
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine(syllabusContext);
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(question.Rubric))
            {
                sb.AppendLine("TEACHER'S RUBRIC (ADDITIONAL GUIDANCE):");
                sb.AppendLine("───────────────────────────────────────────────────────────");
                sb.AppendLine(question.Rubric);
                sb.AppendLine("───────────────────────────────────────────────────────────");
                sb.AppendLine();
            }

            if (question.Keywords.Any())
            {
                sb.AppendLine($"KEY CONCEPTS TO CHECK: {string.Join(", ", question.Keywords)}");
                sb.AppendLine();
            }

            sb.AppendLine("STUDENT'S ANSWER (OCR EXTRACTED - MAY HAVE MINOR ERRORS):");
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine(studentAnswer);
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine();

            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("1. Generate expected answer ONLY from syllabus content above");
            sb.AppendLine("2. Create step-wise marking scheme (sum of step marks = max marks)");
            sb.AppendLine("3. Evaluate student answer against EACH step independently");
            sb.AppendLine("4. Award partial credit where applicable");
            sb.AppendLine("5. Return STRICT JSON response");
            sb.AppendLine();
            sb.AppendLine("EVALUATE NOW:");

            return sb.ToString();
        }

        /// <summary>
        /// Parses the AI response into structured StepWiseQuestionEvaluation.
        /// </summary>
        private StepWiseQuestionEvaluation ParseStepWiseEvaluationResponse(
            string content,
            decimal maxMarks,
            List<int> syllabusChunkIds)
        {
            try
            {
                // Clean response - remove markdown code blocks if present
                var cleanContent = content.Trim();
                if (cleanContent.StartsWith("```"))
                {
                    cleanContent = Regex.Replace(cleanContent, @"^```(?:json)?\s*", "");
                    cleanContent = Regex.Replace(cleanContent, @"\s*```$", "");
                }

                // Extract JSON object
                var jsonMatch = Regex.Match(cleanContent, @"\{[\s\S]*\}", RegexOptions.Multiline);
                if (!jsonMatch.Success)
                {
                    _logger.LogWarning("No JSON found in AI response, returning default");
                    return CreateDefaultEvaluation(maxMarks, syllabusChunkIds);
                }

                var parsed = JsonSerializer.Deserialize<StepWiseQuestionEvaluation>(
                    jsonMatch.Value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed == null)
                {
                    _logger.LogWarning("Failed to deserialize evaluation response");
                    return CreateDefaultEvaluation(maxMarks, syllabusChunkIds);
                }

                // Validate and clamp scores
                ValidateAndClampScores(parsed, maxMarks);

                // Add syllabus chunk IDs
                parsed.ExpectedAnswer.SyllabusChunkIds = syllabusChunkIds;

                return parsed;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "JSON parsing failed for evaluation response");
                return CreateDefaultEvaluation(maxMarks, syllabusChunkIds);
            }
        }

        /// <summary>
        /// Validates and clamps scores to ensure they're within valid range.
        /// </summary>
        private static void ValidateAndClampScores(StepWiseQuestionEvaluation eval, decimal maxMarks)
        {
            // Ensure total awarded marks is within bounds
            eval.StudentEvaluation.TotalAwardedMarks = Math.Max(0,
                Math.Min(eval.StudentEvaluation.TotalAwardedMarks, maxMarks));

            // Validate step scores
            if (eval.StudentEvaluation.Steps.Any())
            {
                foreach (var step in eval.StudentEvaluation.Steps)
                {
                    step.AwardedMarks = Math.Max(0,
                        Math.Min(step.AwardedMarks, step.MaxMarks));
                }

                // Validate expected answer step marks sum to maxMarks
                if (eval.ExpectedAnswer.Steps.Any())
                {
                    var expectedStepsSum = eval.ExpectedAnswer.Steps.Sum(s => s.Marks);
                    if (expectedStepsSum != maxMarks && expectedStepsSum > 0)
                    {
                        // Normalize step marks to sum to maxMarks
                        var ratio = maxMarks / expectedStepsSum;
                        foreach (var step in eval.ExpectedAnswer.Steps)
                        {
                            step.Marks = Math.Round(step.Marks * ratio, 2);
                        }
                    }
                }

                // Recalculate total from steps if mismatch
                var stepsTotal = eval.StudentEvaluation.Steps.Sum(s => s.AwardedMarks);
                if (stepsTotal != eval.StudentEvaluation.TotalAwardedMarks)
                {
                    eval.StudentEvaluation.TotalAwardedMarks = Math.Min(stepsTotal, maxMarks);
                }
            }

            // Clamp confidence score
            eval.StudentEvaluation.ConfidenceScore = Math.Max(0,
                Math.Min(eval.StudentEvaluation.ConfidenceScore, 1.0m));
        }

        /// <summary>
        /// Creates a default evaluation when parsing fails.
        /// </summary>
        private static StepWiseQuestionEvaluation CreateDefaultEvaluation(
            decimal maxMarks,
            List<int> syllabusChunkIds)
        {
            return new StepWiseQuestionEvaluation
            {
                MaxMarks = maxMarks,
                ExpectedAnswer = new ExpectedAnswer
                {
                    Summary = "Unable to generate expected answer.",
                    Steps = new List<MarkingStep>
                    {
                        new MarkingStep
                        {
                            StepNumber = 1,
                            Description = "Complete answer",
                            Keywords = new List<string>(),
                            Marks = maxMarks
                        }
                    },
                    SyllabusChunkIds = syllabusChunkIds
                },
                StudentEvaluation = new StudentStepWiseEvaluation
                {
                    Steps = new List<StepEvaluation>
                    {
                        new StepEvaluation
                        {
                            StepNumber = 1,
                            AwardedMarks = 0,
                            MaxMarks = maxMarks,
                            Reason = "Evaluation failed. Manual review required."
                        }
                    },
                    TotalAwardedMarks = 0,
                    ConfidenceScore = 0
                },
                OverallFeedback = "Automated evaluation encountered an error. Please review manually."
            };
        }

        /// <summary>
        /// Segments OCR text into per-question answers using AI.
        /// </summary>
        private async Task<Dictionary<int, string>> SegmentAnswersByQuestionAsync(
            string extractedText,
            List<ExamQuestionWithRubric> questions,
            Guid submissionId,
            CancellationToken cancellationToken)
        {
            var prompt = BuildSegmentationPrompt(extractedText, questions);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var options = new ChatCompletionsOptions
            {
                DeploymentName = _deploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage(
                        "You are an expert at analyzing handwritten exam answers. " +
                        "Your task is to segment OCR-extracted text into individual question answers. " +
                        "Return ONLY valid JSON."),
                    new ChatRequestUserMessage(prompt)
                },
                Temperature = 0.1f,
                MaxTokens = 4000
            };

            var response = await _openAiClient.GetChatCompletionsAsync(options, cts.Token);
            var content = response.Value.Choices[0].Message.Content;

            try
            {
                // Extract JSON from response
                var jsonMatch = Regex.Match(content, @"\{[\s\S]*\}", RegexOptions.Multiline);
                if (jsonMatch.Success)
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        jsonMatch.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    var result = new Dictionary<int, string>();
                    if (parsed != null)
                    {
                        foreach (var kvp in parsed)
                        {
                            if (int.TryParse(kvp.Key.Replace("Q", "").Replace("q", ""), out int qNum))
                            {
                                result[qNum] = kvp.Value;
                            }
                        }
                    }

                    _logger.LogInformation(
                        "[{SubmissionId}] Segmented {AnswerCount} answers from OCR text",
                        submissionId, result.Count);

                    return result;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "[{SubmissionId}] Failed to parse segmentation response, using fallback",
                    submissionId);
            }

            // Fallback: return entire text for each question
            return questions.ToDictionary(q => q.QuestionNumber, _ => extractedText);
        }

        /// <summary>
        /// Builds the segmentation prompt.
        /// </summary>
        private static string BuildSegmentationPrompt(
            string extractedText,
            List<ExamQuestionWithRubric> questions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyze the following OCR-extracted text from a student's handwritten exam.");
            sb.AppendLine("Segment the text into answers for each question listed below.");
            sb.AppendLine();
            sb.AppendLine("QUESTIONS:");
            foreach (var q in questions.OrderBy(q => q.QuestionNumber))
            {
                sb.AppendLine($"Q{q.QuestionNumber}: {q.QuestionText}");
            }
            sb.AppendLine();
            sb.AppendLine("OCR EXTRACTED TEXT:");
            sb.AppendLine("---");
            sb.AppendLine(extractedText);
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("Return a JSON object mapping question numbers to student answers.");
            sb.AppendLine("Format: { \"Q1\": \"student's answer for Q1\", \"Q2\": \"student's answer for Q2\", ... }");
            sb.AppendLine("If an answer is not found, use \"[Not answered]\".");

            return sb.ToString();
        }

        /// <summary>
        /// Calculate letter grade from percentage.
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
