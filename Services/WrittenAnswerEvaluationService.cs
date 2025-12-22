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
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(20); // 20s timeout per question (ultra-fast)

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
            _logger.LogWarning(
                "[EVAL-START] [{SubmissionId}] === STARTING EVALUATION === Questions: {QuestionCount}, ExtractedTextLength: {TextLength}",
                submission.Id, questions.Count, extractedText?.Length ?? 0);
            
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
            _logger.LogWarning("[EVAL-SEGMENT] [{SubmissionId}] Starting answer segmentation...", submission.Id);
            var segmentedAnswers = await SegmentAnswersByQuestionAsync(
                extractedText, questions, submission.Id, cancellationToken);
            _logger.LogWarning("[EVAL-SEGMENT] [{SubmissionId}] Segmentation complete. Found {Count} answers", submission.Id, segmentedAnswers.Count);

            // Step 2: ULTRA-FAST BATCH EVALUATION - Send 5 questions per OpenAI call
            // This reduces API calls from N to N/5, dramatically improving speed
            const int questionsPerApiCall = 5; // 5 questions per single API call
            const int parallelApiBatches = 4;  // 4 API calls in parallel = 20 questions simultaneously
            var evaluations = new List<WrittenQuestionEvaluation>();
            var orderedQuestions = questions.OrderBy(q => q.QuestionNumber).ToList();
            
            _logger.LogInformation(
                "[{SubmissionId}] Starting BATCH EVALUATION: {QuestionCount} questions, {QuestionsPerCall} per API call, {ParallelBatches} parallel calls",
                submission.Id, orderedQuestions.Count, questionsPerApiCall, parallelApiBatches);

            // Group questions into batches of 5 for single API calls
            var questionBatches = orderedQuestions
                .Select((q, i) => new { Question = q, Index = i })
                .GroupBy(x => x.Index / questionsPerApiCall)
                .Select(g => g.Select(x => x.Question).ToList())
                .ToList();

            // Process API batches in parallel groups
            for (int i = 0; i < questionBatches.Count; i += parallelApiBatches)
            {
                var parallelBatches = questionBatches.Skip(i).Take(parallelApiBatches).ToList();
                
                var batchTasks = parallelBatches.Select(async batch =>
                {
                    var batchAnswers = batch.Select(q => new {
                        Question = q,
                        Answer = segmentedAnswers.GetValueOrDefault(q.QuestionNumber, "[Answer not found]")
                    }).ToList();

                    try
                    {
                        return await EvaluateQuestionBatchAsync(
                            submission.Id,
                            batchAnswers.Select(b => b.Question).ToList(),
                            batchAnswers.Select(b => b.Answer).ToList(),
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[{SubmissionId}] Batch evaluation failed, using fallback", submission.Id);
                        // Fallback: Return zero scores for failed batch
                        return batch.Select(q => new WrittenQuestionEvaluation
                        {
                            Id = Guid.NewGuid(),
                            WrittenSubmissionId = submission.Id,
                            QuestionId = q.QuestionId,
                            QuestionNumber = q.QuestionNumber,
                            ExtractedAnswer = segmentedAnswers.GetValueOrDefault(q.QuestionNumber, ""),
                            MaxScore = q.MaxScore,
                            AwardedScore = 0,
                            Feedback = "Evaluation failed",
                            RubricBreakdown = "{}",
                            EvaluatedAt = DateTime.UtcNow
                        }).ToList();
                    }
                }).ToList();

                var batchResults = await Task.WhenAll(batchTasks);
                foreach (var batchResult in batchResults)
                {
                    evaluations.AddRange(batchResult);
                }
                
                _logger.LogInformation(
                    "[{SubmissionId}] Parallel batch {BatchNum}/{TotalBatches} complete",
                    submission.Id, (i / parallelApiBatches) + 1, (questionBatches.Count + parallelApiBatches - 1) / parallelApiBatches);
            }

            // Calculate totals from parallel results
            decimal totalScore = evaluations.Sum(e => e.AwardedScore);
            decimal maxPossibleScore = evaluations.Sum(e => e.MaxScore);
            
            // Calculate separate MCQ and Subjective scores
            var mcqEvaluations = evaluations.Where(e => e.IsMcq).ToList();
            var subjectiveEvaluations = evaluations.Where(e => !e.IsMcq).ToList();

            result.QuestionEvaluations = evaluations.OrderBy(e => e.QuestionNumber).ToList();
            result.TotalScore = totalScore;
            result.MaxPossibleScore = maxPossibleScore;
            result.Percentage = maxPossibleScore > 0
                ? Math.Round((totalScore / maxPossibleScore) * 100, 2)
                : 0;
            result.Grade = CalculateGrade(result.Percentage);
            
            // Set MCQ scores
            result.McqScore = mcqEvaluations.Sum(e => e.AwardedScore);
            result.McqMaxScore = mcqEvaluations.Sum(e => e.MaxScore);
            result.McqCount = mcqEvaluations.Count;
            
            // Set Subjective scores
            result.SubjectiveScore = subjectiveEvaluations.Sum(e => e.AwardedScore);
            result.SubjectiveMaxScore = subjectiveEvaluations.Sum(e => e.MaxScore);
            result.SubjectiveCount = subjectiveEvaluations.Count;

            _logger.LogInformation(
                "[{SubmissionId}] BOARD BLUEPRINT evaluation completed: {TotalScore}/{MaxScore} ({Percentage}%) Grade={Grade} | MCQ: {McqScore}/{McqMax} ({McqCount}Q) | Subjective: {SubjScore}/{SubjMax} ({SubjCount}Q)",
                submission.Id, totalScore, maxPossibleScore, result.Percentage, result.Grade, 
                result.McqScore, result.McqMaxScore, result.McqCount,
                result.SubjectiveScore, result.SubjectiveMaxScore, result.SubjectiveCount);

            return result;
        }

        /// <summary>
        /// ULTRA-FAST: Evaluates multiple questions in a SINGLE OpenAI API call.
        /// This reduces API latency from N calls to 1 call for N questions.
        /// Handles both MCQ and subjective questions appropriately.
        /// </summary>
        private async Task<List<WrittenQuestionEvaluation>> EvaluateQuestionBatchAsync(
            Guid submissionId,
            List<ExamQuestionWithRubric> questions,
            List<string> studentAnswers,
            CancellationToken cancellationToken)
        {
            var evaluations = new List<WrittenQuestionEvaluation>();
            
            // Separate MCQ and Subjective questions
            var mcqQuestions = new List<(ExamQuestionWithRubric Question, string Answer, int Index)>();
            var subjectiveQuestions = new List<(ExamQuestionWithRubric Question, string Answer, int Index)>();
            
            for (int i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                var answer = studentAnswers[i];
                
                // Use database IsMcq field first, fallback to text pattern detection
                bool isMcq = q.IsMcq || IsMcqQuestion(q.QuestionText);
                q.IsMcq = isMcq;
                
                if (isMcq)
                {
                    mcqQuestions.Add((q, answer, i));
                }
                else
                {
                    subjectiveQuestions.Add((q, answer, i));
                }
            }
            
            _logger.LogInformation(
                "[{SubmissionId}] Batch contains {McqCount} MCQ and {SubjectiveCount} subjective questions",
                submissionId, mcqQuestions.Count, subjectiveQuestions.Count);
            
            // Evaluate MCQ questions with exact matching
            foreach (var (q, answer, index) in mcqQuestions)
            {
                evaluations.Add(EvaluateMcqQuestion(submissionId, q, answer));
            }
            
            // Evaluate subjective questions with AI (only if there are any)
            if (subjectiveQuestions.Count > 0)
            {
                var subjectiveEvals = await EvaluateSubjectiveBatchAsync(
                    submissionId, 
                    subjectiveQuestions.Select(x => x.Question).ToList(),
                    subjectiveQuestions.Select(x => x.Answer).ToList(),
                    cancellationToken);
                evaluations.AddRange(subjectiveEvals);
            }
            
            return evaluations.OrderBy(e => e.QuestionNumber).ToList();
        }
        
        /// <summary>
        /// Detects if a question is MCQ by looking for option markers
        /// </summary>
        private bool IsMcqQuestion(string questionText)
        {
            if (string.IsNullOrWhiteSpace(questionText))
                return false;
                
            // Look for MCQ option patterns: (A), (B), (C), (D) or a), b), c), d) or A., B., C., D.
            var mcqPattern = @"(?i)(\(?\s*[A-Da-d]\s*[\)\.:]|^[A-Da-d][\)\.:]|\n\s*[A-Da-d][\)\.:]|[A-Da-d]\)\s+\w)";
            var matches = Regex.Matches(questionText, mcqPattern);
            
            // If we find at least 2 option markers, it's likely an MCQ
            return matches.Count >= 2;
        }
        
        /// <summary>
        /// Evaluates MCQ question with exact matching logic
        /// </summary>
        private WrittenQuestionEvaluation EvaluateMcqQuestion(
            Guid submissionId,
            ExamQuestionWithRubric question,
            string studentAnswer)
        {
            _logger.LogInformation(
                "[{SubmissionId}] Evaluating MCQ Q{QuestionNumber} with exact matching",
                submissionId, question.QuestionNumber);
            
            // Extract student's answer (look for A, B, C, D or a, b, c, d)
            var studentChoice = ExtractMcqChoice(studentAnswer);
            var correctChoice = ExtractMcqChoice(question.ModelAnswer);
            
            bool isCorrect = !string.IsNullOrEmpty(studentChoice) && 
                            !string.IsNullOrEmpty(correctChoice) &&
                            studentChoice.Equals(correctChoice, StringComparison.OrdinalIgnoreCase);
            
            decimal awardedScore = isCorrect ? question.MaxScore : 0;
            
            string feedback;
            bool wasNotAnswered = string.IsNullOrWhiteSpace(studentAnswer) || 
                                 studentAnswer.Contains("[Not answered]") || 
                                 studentAnswer.Contains("[Answer not found]") ||
                                 studentAnswer.Trim().Length < 1;
            
            if (wasNotAnswered)
            {
                feedback = "Question not answered.";
                awardedScore = 0;
            }
            else if (string.IsNullOrEmpty(studentChoice))
            {
                feedback = $"No valid option found in answer. Extracted text: '{studentAnswer.Substring(0, Math.Min(50, studentAnswer.Length))}...'. Correct answer: {correctChoice}.";
                awardedScore = 0;
            }
            else if (isCorrect)
            {
                feedback = $"Correct! Selected option {studentChoice.ToUpper()}.";
            }
            else
            {
                feedback = $"Incorrect. Selected: {studentChoice.ToUpper()}, Correct answer: {correctChoice.ToUpper()}.";
            }
            
            // MCQ questions don't need rubricBreakdown - just return score and feedback
            return new WrittenQuestionEvaluation
            {
                Id = Guid.NewGuid(),
                WrittenSubmissionId = submissionId,
                QuestionId = question.QuestionId,
                QuestionNumber = question.QuestionNumber,
                QuestionText = question.QuestionText,
                ExtractedAnswer = studentAnswer,
                ModelAnswer = question.ModelAnswer,
                MaxScore = question.MaxScore,
                AwardedScore = awardedScore,
                Feedback = feedback,
                RubricBreakdown = "", // No rubric breakdown for MCQ questions
                EvaluatedAt = DateTime.UtcNow,
                IsMcq = true
            };
        }
        
        /// <summary>
        /// Extracts MCQ choice (A, B, C, D) from answer text
        /// </summary>
        private string ExtractMcqChoice(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            
            // Check if answer is marked as not found/answered
            if (text.Contains("[Answer not found]") || text.Contains("[Not answered]"))
                return string.Empty;
            
            // Trim and clean the text
            text = text.Trim();
            
            // Look for explicit patterns: "option A", "Answer: B", "(C)", "D)"
            var explicitPattern = @"(?i)(?:option|answer|ans|choice)[\s:]+\(?\s*([A-Da-d])\s*\)?";
            var match = Regex.Match(text, explicitPattern);
            
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpper();
            }
            
            // Look for parenthesized choices: (A), (B), (C), (D)
            match = Regex.Match(text, @"(?i)\(\s*([A-Da-d])\s*\)");
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpper();
            }
            
            // Look for choices with closing paren: A), B), C), D)
            match = Regex.Match(text, @"(?i)^\s*([A-Da-d])\s*\)");
            if (match.Success)
            {
                return match.Groups[1].Value.ToUpper();
            }
            
            // Only if text is very short (1-3 chars), check if it's just a letter
            if (text.Length <= 3)
            {
                match = Regex.Match(text, @"^\s*([A-Da-d])\s*$");
                if (match.Success)
                {
                    return match.Groups[1].Value.ToUpper();
                }
            }
            
            return string.Empty;
        }
        
        /// <summary>
        /// Evaluates subjective questions using AI
        /// </summary>
        private async Task<List<WrittenQuestionEvaluation>> EvaluateSubjectiveBatchAsync(
            Guid submissionId,
            List<ExamQuestionWithRubric> questions,
            List<string> studentAnswers,
            CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(45)); // 45s for batch of 5 questions

            // Build batch prompt with all questions
            var batchPrompt = new StringBuilder();
            batchPrompt.AppendLine("Evaluate the following student answers. For EACH question, provide a JSON object with:");
            batchPrompt.AppendLine("- questionNumber, awardedScore, maxScore, feedback, stepWiseBreakdown (array of steps with marks)");
            batchPrompt.AppendLine();
            batchPrompt.AppendLine("EVALUATION RULES (STATE BOARD STYLE):");
            batchPrompt.AppendLine("1. Treat the 'Max' value for each question as TOTAL marks for that question.");
            batchPrompt.AppendLine("2. Break the marking into 1-mark style STEPS (or groups of marks) so that the SUM of step maxMarks == question maxScore.");
            batchPrompt.AppendLine("3. Award partial credit step-wise – each step has: step, awardedMarks, maxMarks, comment.");
            batchPrompt.AppendLine("4. Comments must read like REAL examiner remarks (e.g., 'Concept identified but explanation incomplete', 'Correct formula but wrong substitution').");
            batchPrompt.AppendLine("5. For blank / not found answers, award 0 for all steps and explain clearly that the answer is missing.");
            batchPrompt.AppendLine();
            batchPrompt.AppendLine("Return a JSON array of evaluations, one per question.");
            batchPrompt.AppendLine();

            for (int i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                var answer = studentAnswers[i];
                batchPrompt.AppendLine($"--- QUESTION {q.QuestionNumber} (Max: {q.MaxScore} marks) ---");
                batchPrompt.AppendLine($"Q: {q.QuestionText}");
                if (!string.IsNullOrWhiteSpace(q.ModelAnswer))
                {
                    batchPrompt.AppendLine($"Expected Answer: {q.ModelAnswer}");
                }
                if (!string.IsNullOrWhiteSpace(q.Rubric))
                {
                    batchPrompt.AppendLine($"Marking Rubric: {q.Rubric}");
                }
                if (q.Keywords.Any())
                {
                    batchPrompt.AppendLine($"Key Concepts: {string.Join(", ", q.Keywords)}");
                }
                batchPrompt.AppendLine($"Student Answer: {answer}");
                batchPrompt.AppendLine();
            }

            batchPrompt.AppendLine(@"Return ONLY a JSON array like: 
[{
  ""questionNumber"": 1,
  ""awardedScore"": 2.5,
  ""maxScore"": 5,
  ""feedback"": ""Good attempt but missed key concepts..."",
  ""stepWiseBreakdown"": [
    {""step"": ""Understanding of concept"", ""awardedMarks"": 1, ""maxMarks"": 2, ""comment"": ""Partial understanding shown""},
    {""step"": ""Application"", ""awardedMarks"": 1.5, ""maxMarks"": 3, ""comment"": ""Correct method but calculation error""}
  ]
},...]");

            var options = new ChatCompletionsOptions
            {
                DeploymentName = _deploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage(@"You are an experienced STATE BOARD EXAMINER.
Evaluate ONLY subjective / long answers using BLUEPRINT STYLE STEP-WISE MARKING.

YOUR RESPONSIBILITY:
- Read each question and its Max marks carefully.
- Imagine the official board marking scheme for that question.
- Split the total marks into clear steps (method, key points, diagram, conclusion, etc.).
- For each step, decide how many marks it carries (maxMarks) and how many the student gets (awardedMarks).

STEP-WISE MARKING RULES:
1. Sum of all step maxMarks MUST EQUAL the question's maxScore.
2. Sum of awardedMarks across steps MUST EQUAL awardedScore (and must be between 0 and maxScore).
3. Award method marks even if final answer is wrong (correct process, wrong final value).
4. Give credit when key concepts/points are present even if language is different.
5. Ignore minor spelling / OCR mistakes – focus on understanding and steps.
6. For blank / not-attempted answers, all steps get 0 with a clear comment like 'Answer not written' or 'Answer not found in pages'.

FEEDBACK STYLE:
- The comment for each step must look like a REAL teacher remark for that specific step.
- The overall feedback must be 2–3 sentences, clear for both teacher and student.
- Avoid generic phrases like 'good', 'ok', 'needs improvement' – always mention WHAT is missing or correct.
"),
                    new ChatRequestUserMessage(batchPrompt.ToString())
                },
                Temperature = 0.0f,
                MaxTokens = 2500 // Increased for detailed rubric breakdown (~500 tokens per question)
            };

            var apiStart = DateTime.UtcNow;
            var response = await _openAiClient.GetChatCompletionsAsync(options, cts.Token);
            var apiDuration = (DateTime.UtcNow - apiStart).TotalMilliseconds;
            
            _logger.LogInformation(
                "[BATCH-EVAL] {QuestionCount} questions evaluated in {DurationMs}ms",
                questions.Count, apiDuration);

            var content = response.Value.Choices[0].Message.Content ?? "[]";
            
            // Parse batch response
            var evaluations = ParseBatchEvaluationResponse(submissionId, questions, studentAnswers, content);
            return evaluations;
        }

        /// <summary>
        /// Parses the batch evaluation JSON response.
        /// </summary>
        private List<WrittenQuestionEvaluation> ParseBatchEvaluationResponse(
            Guid submissionId,
            List<ExamQuestionWithRubric> questions,
            List<string> studentAnswers,
            string content)
        {
            var evaluations = new List<WrittenQuestionEvaluation>();
            
            try
            {
                // Clean and extract JSON array
                var cleanContent = content.Trim();
                if (cleanContent.StartsWith("```"))
                {
                    cleanContent = Regex.Replace(cleanContent, @"^```(?:json)?\s*", "");
                    cleanContent = Regex.Replace(cleanContent, @"\s*```$", "");
                }

                var jsonMatch = Regex.Match(cleanContent, @"\[[\s\S]*\]", RegexOptions.Multiline);
                if (jsonMatch.Success)
                {
                    var results = JsonSerializer.Deserialize<List<BatchEvalResult>>(
                        jsonMatch.Value,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (results != null)
                    {
                        foreach (var q in questions)
                        {
                            var result = results.FirstOrDefault(r => r.QuestionNumber == q.QuestionNumber);
                            var answer = studentAnswers[questions.IndexOf(q)];

                            // Normalize step-wise marks to align with question max score
                            if (result != null)
                            {
                                NormalizeBatchStepWiseMarks(q.MaxScore, result);
                            }
                            
                            // Build comprehensive rubric breakdown
                            var rubricBreakdown = new
                            {
                                questionNumber = q.QuestionNumber,
                                maxScore = q.MaxScore,
                                awardedScore = result?.AwardedScore ?? 0,
                                extractedAnswer = answer,
                                stepWiseBreakdown = result?.StepWiseBreakdown ?? new List<StepWiseBreakdownItem>(),
                                keywords = q.Keywords,
                                rubric = q.Rubric,
                                evaluationTimestamp = DateTime.UtcNow
                            };
                            
                            var rubricJson = JsonSerializer.Serialize(rubricBreakdown, new JsonSerializerOptions 
                            { 
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                WriteIndented = false 
                            });
                            
                            evaluations.Add(new WrittenQuestionEvaluation
                            {
                                Id = Guid.NewGuid(),
                                WrittenSubmissionId = submissionId,
                                QuestionId = q.QuestionId,
                                QuestionNumber = q.QuestionNumber,
                                QuestionText = q.QuestionText,
                                ExtractedAnswer = answer,
                                ModelAnswer = q.ModelAnswer,
                                MaxScore = q.MaxScore,
                                AwardedScore = Math.Min(result?.AwardedScore ?? 0, q.MaxScore),
                                Feedback = !string.IsNullOrWhiteSpace(result?.Feedback) 
                                    ? result.Feedback 
                                    : (answer.Contains("[Not answered]") || answer.Contains("[Answer not found]") 
                                        ? "Question not answered or answer not found in submission." 
                                        : "Evaluated based on model answer and rubric."),
                                RubricBreakdown = rubricJson,
                                EvaluatedAt = DateTime.UtcNow
                            });
                        }
                        return evaluations;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse batch evaluation response");
            }

            // Fallback: Create zero-score evaluations with proper rubric structure
            foreach (var q in questions)
            {
                var answer = studentAnswers[questions.IndexOf(q)];
                var rubricBreakdown = new
                {
                    questionNumber = q.QuestionNumber,
                    maxScore = q.MaxScore,
                    awardedScore = 0,
                    extractedAnswer = answer,
                    stepWiseBreakdown = new[]
                    {
                        new { step = "Overall evaluation", awardedMarks = 0, maxMarks = (double)q.MaxScore, comment = "Evaluation parsing failed or API error" }
                    },
                    keywords = q.Keywords,
                    rubric = q.Rubric,
                    evaluationTimestamp = DateTime.UtcNow,
                    fallbackUsed = true
                };
                
                var rubricJson = JsonSerializer.Serialize(rubricBreakdown, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false 
                });
                
                evaluations.Add(new WrittenQuestionEvaluation
                {
                    Id = Guid.NewGuid(),
                    WrittenSubmissionId = submissionId,
                    QuestionId = q.QuestionId,
                    QuestionNumber = q.QuestionNumber,
                    QuestionText = q.QuestionText,
                    ExtractedAnswer = answer,
                    ModelAnswer = q.ModelAnswer,
                    MaxScore = q.MaxScore,
                    AwardedScore = 0,
                    Feedback = answer.Contains("[Not answered]") || answer.Contains("[Answer not found]") 
                        ? "Question not answered or answer not found in submission." 
                        : "Evaluation system encountered an error. Please review manually.",
                    RubricBreakdown = rubricJson,
                    EvaluatedAt = DateTime.UtcNow
                });
            }
            return evaluations;
        }

        /// <summary>
        /// Helper class for parsing batch evaluation results.
        /// </summary>
        private class BatchEvalResult
        {
            public int QuestionNumber { get; set; }
            public decimal AwardedScore { get; set; }
            public decimal MaxScore { get; set; }
            public string Feedback { get; set; } = "";
            public List<StepWiseBreakdownItem>? StepWiseBreakdown { get; set; }
        }
        
        /// <summary>
        /// Helper class for step-wise marking breakdown.
        /// </summary>
        private class StepWiseBreakdownItem
        {
            public string Step { get; set; } = "";
            public decimal AwardedMarks { get; set; }
            public decimal MaxMarks { get; set; }
            public string Comment { get; set; } = "";
        }

        /// <summary>
        /// Normalizes batch step-wise marks so that they follow the
        /// question's total marks (maxScore) and behave like a real
        /// board marking scheme.
        /// </summary>
        private static void NormalizeBatchStepWiseMarks(decimal questionMaxScore, BatchEvalResult result)
        {
            if (result.StepWiseBreakdown == null || !result.StepWiseBreakdown.Any())
            {
                // Nothing to normalize
                result.AwardedScore = Math.Max(0, Math.Min(result.AwardedScore, questionMaxScore));
                return;
            }

            var steps = result.StepWiseBreakdown;

            // Ensure all maxMarks are non-negative
            foreach (var step in steps)
            {
                if (step.MaxMarks < 0)
                {
                    step.MaxMarks = 0;
                }
            }

            // Normalize maxMarks so their sum equals questionMaxScore
            var totalMax = steps.Sum(s => s.MaxMarks);
            if (totalMax <= 0 && questionMaxScore > 0 && steps.Count > 0)
            {
                var equalMarks = questionMaxScore / steps.Count;
                foreach (var step in steps)
                {
                    step.MaxMarks = Math.Round(equalMarks, 2);
                }
            }
            else if (totalMax > 0 && totalMax != questionMaxScore)
            {
                var ratio = questionMaxScore / totalMax;
                foreach (var step in steps)
                {
                    step.MaxMarks = Math.Round(step.MaxMarks * ratio, 2);
                }
            }

            // Clamp awardedMarks within [0, maxMarks]
            foreach (var step in steps)
            {
                step.AwardedMarks = Math.Max(0, Math.Min(step.AwardedMarks, step.MaxMarks));
            }

            // If total awarded from steps exceeds questionMaxScore, scale down proportionally
            var stepsAwardedTotal = steps.Sum(s => s.AwardedMarks);
            if (stepsAwardedTotal > questionMaxScore && stepsAwardedTotal > 0)
            {
                var ratio = questionMaxScore / stepsAwardedTotal;
                foreach (var step in steps)
                {
                    step.AwardedMarks = Math.Round(step.AwardedMarks * ratio, 2);
                }
                stepsAwardedTotal = steps.Sum(s => s.AwardedMarks);
            }

            // Final awarded score comes from step-wise total, clamped to [0, questionMaxScore]
            result.AwardedScore = Math.Max(0, Math.Min(stepsAwardedTotal, questionMaxScore));
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
            _logger.LogWarning(
                "[EVAL-SINGLE] [{SubmissionId}] Q{QuestionNumber} - METHOD ENTRY - Creating timeout ({TimeoutSeconds}s)",
                submissionId, question.QuestionNumber, _timeout.TotalSeconds);
            
            // Create timeout for entire evaluation (RAG + OpenAI)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            _logger.LogWarning(
                "[EVAL-RAG] [{SubmissionId}] Q{QuestionNumber} - Calling RAG service with fallback...",
                submissionId, question.QuestionNumber);
            
            // Step 1: Fetch relevant syllabus chunks using RAG (with timeout and fallback)
            // SPEED OPTIMIZATION: Skip RAG if we have a model answer - use it directly
            List<SyllabusChunk> syllabusChunks = new List<SyllabusChunk>();
            
            // Only do RAG lookup if no model answer (saves ~500ms per question)
            bool hasModelAnswer = !string.IsNullOrWhiteSpace(question.ModelAnswer) && question.ModelAnswer.Length > 10;
            
            if (!hasModelAnswer)
            {
                try
                {
                    syllabusChunks = await _syllabusRagService.GetRelevantSyllabusChunksAsync(
                        question.QuestionText,
                        question.ClassName,
                        question.Subject,
                        question.Chapter,
                        topN: 3, // Reduced from 5 to 3 for speed
                        cts.Token);

                    _logger.LogWarning(
                        "[EVAL-RAG] [{SubmissionId}] Q{QuestionNumber} - RAG COMPLETED - Retrieved {ChunkCount} chunks",
                        submissionId, question.QuestionNumber, syllabusChunks.Count);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning(
                        "[EVAL-RAG] [{SubmissionId}] Q{QuestionNumber} - RAG TIMEOUT - Using model answer as fallback",
                        submissionId, question.QuestionNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[EVAL-RAG] [{SubmissionId}] Q{QuestionNumber} - RAG FAILED - Using fallback: {Error}",
                        submissionId, question.QuestionNumber, ex.Message);
                }
            }
            else
            {
                _logger.LogDebug(
                    "[EVAL-SKIP-RAG] [{SubmissionId}] Q{QuestionNumber} - Model answer available, skipping RAG",
                    submissionId, question.QuestionNumber);
            }

            var syllabusContext = syllabusChunks.Any()
                ? string.Join("\n\n---\n\n", syllabusChunks.Select(c => c.ChunkText))
                : question.ModelAnswer; // Fallback to model answer if no syllabus

            var syllabusChunkIds = syllabusChunks.Select(c => c.ChunkId).ToList();
            
            _logger.LogDebug(
                "[{SubmissionId}] Q{QuestionNumber}: Retrieved {ChunkCount} syllabus chunks",
                submissionId, question.QuestionNumber, syllabusChunks.Count);

            _logger.LogWarning(
                "[EVAL-PROMPT] [{SubmissionId}] Q{QuestionNumber} - Building evaluation prompt...",
                submissionId, question.QuestionNumber);
            
            // Step 2: Build comprehensive evaluation prompt
            var prompt = BuildStepWiseEvaluationPrompt(
                question,
                studentAnswer,
                syllabusContext);

            // Step 3: Call OpenAI (SINGLE CALL per question)
            _logger.LogWarning(
                "[EVAL-OPENAI] [{SubmissionId}] Q{QuestionNumber} - Calling Azure OpenAI API... (Deployment: {Deployment})",
                submissionId, question.QuestionNumber, _deploymentName);

            var options = new ChatCompletionsOptions
            {
                DeploymentName = _deploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage(GetBoardBlueprintSystemPrompt()),
                    new ChatRequestUserMessage(prompt)
                },
                Temperature = 0.0f, // Zero temperature for fastest, most deterministic output
                MaxTokens = 800 // Reduced to 800 - typical response is ~500 tokens
            };

            var apiCallStart = DateTime.UtcNow;
            var response = await _openAiClient.GetChatCompletionsAsync(options, cts.Token);
            var apiCallDuration = DateTime.UtcNow - apiCallStart;
            
            _logger.LogWarning(
                "[EVAL-OPENAI] [{SubmissionId}] Q{QuestionNumber} - OpenAI API COMPLETED in {DurationMs}ms",
                submissionId, question.QuestionNumber, apiCallDuration.TotalMilliseconds);
            
            var content = response.Value.Choices[0].Message.Content;
            
            _logger.LogWarning(
                "[EVAL-PARSE] [{SubmissionId}] Q{QuestionNumber} - Parsing response (length: {ResponseLength})...",
                submissionId, question.QuestionNumber, content?.Length ?? 0);

            // Step 4: Parse structured response
            var evalResult = ParseStepWiseEvaluationResponse(
                content,
                question.MaxScore,
                syllabusChunkIds);

            _logger.LogWarning(
                "[EVAL-SINGLE] [{SubmissionId}] Q{QuestionNumber} - METHOD EXIT - Awarded: {Score}/{MaxScore}",
                submissionId, question.QuestionNumber, evalResult.StudentEvaluation?.TotalAwardedMarks ?? 0, question.MaxScore);

            return new WrittenQuestionEvaluation
            {
                Id = Guid.NewGuid(),
                WrittenSubmissionId = submissionId,
                QuestionId = question.QuestionId,
                QuestionNumber = question.QuestionNumber,
                QuestionText = question.QuestionText,
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
        ""studentWritten"": ""<EXACT line/content student wrote for this step>"",
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

            // Parse student answer into individual lines/steps for better evaluation
            var studentLines = studentAnswer.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            sb.AppendLine("STUDENT'S ANSWER (OCR EXTRACTED - MAY HAVE MINOR ERRORS):");
            sb.AppendLine("───────────────────────────────────────────────────────────");
            
            if (studentLines.Count > 1)
            {
                // Multi-line answer - show each line as a step
                sb.AppendLine("Student wrote the following lines:");
                for (int i = 0; i < studentLines.Count; i++)
                {
                    sb.AppendLine($"  Line {i + 1}: {studentLines[i]}");
                }
            }
            else
            {
                // Single line answer
                sb.AppendLine(studentAnswer);
            }
            
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine();

            sb.AppendLine("INSTRUCTIONS:");
            sb.AppendLine("1. Generate expected answer ONLY from syllabus content above");
            sb.AppendLine("2. Create step-wise marking scheme (sum of step marks = max marks)");
            sb.AppendLine("3. Evaluate student answer against EACH step independently");
            sb.AppendLine("4. In stepWiseBreakdown, include the ACTUAL LINE the student wrote for each step");
            sb.AppendLine("5. Award partial credit where applicable");
            sb.AppendLine("6. Return STRICT JSON response");
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
        /// Segments OCR text into per-question answers using pattern matching and AI.
        /// </summary>
        private async Task<Dictionary<int, string>> SegmentAnswersByQuestionAsync(
            string extractedText,
            List<ExamQuestionWithRubric> questions,
            Guid submissionId,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "[{SubmissionId}] Starting answer segmentation. OCR text length: {TextLength}, Questions: {QuestionCount}",
                submissionId, extractedText?.Length ?? 0, questions.Count);

            // First, try pattern-based heuristic segmentation (fast, no API call)
            var heuristicResult = TryHeuristicSegmentation(extractedText, questions, submissionId);
            if (heuristicResult != null && heuristicResult.Count == questions.Count)
            {
                _logger.LogInformation(
                    "[{SubmissionId}] Heuristic segmentation successful, found {AnswerCount} answers",
                    submissionId, heuristicResult.Count);
                return heuristicResult;
            }

            // If heuristic fails, use AI segmentation
            _logger.LogInformation(
                "[{SubmissionId}] Heuristic segmentation incomplete, using AI segmentation",
                submissionId);

            var prompt = BuildSegmentationPrompt(extractedText, questions);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30s for segmentation

            var options = new ChatCompletionsOptions
            {
                DeploymentName = _deploymentName,
                Messages =
                {
                    new ChatRequestSystemMessage(
                        "You are an expert at analyzing handwritten exam answers from OCR text. " +
                        "Your task is to identify where each question's answer starts and ends in the OCR text. " +
                        "Look for question markers like 'Q1', 'Question 1', '1)', '1.', 'Ans 1', etc. " +
                        "OCR may have errors - be flexible with spacing and formatting. " +
                        "Return ONLY valid JSON mapping question numbers to their answers."),
                    new ChatRequestUserMessage(prompt)
                },
                Temperature = 0.0f,
                MaxTokens = 4000
            };

            try
            {
                var response = await _openAiClient.GetChatCompletionsAsync(options, cts.Token);
                var content = response.Value.Choices[0].Message.Content;

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
                            // Extract question number from key (handles Q1, q1, Question1, 1, etc.)
                            var numStr = Regex.Replace(kvp.Key, @"[^0-9]", "");
                            if (int.TryParse(numStr, out int qNum))
                            {
                                result[qNum] = kvp.Value?.Trim() ?? "";
                            }
                        }
                    }

                    if (result.Count > 0)
                    {
                        _logger.LogInformation(
                            "[{SubmissionId}] AI segmentation successful, segmented {AnswerCount}/{QuestionCount} answers",
                            submissionId, result.Count, questions.Count);
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[{SubmissionId}] AI segmentation failed: {Error}",
                    submissionId, ex.Message);
            }

            // Final fallback: Use heuristic result if available, otherwise mark as not found
            if (heuristicResult != null && heuristicResult.Count > 0)
            {
                _logger.LogInformation(
                    "[{SubmissionId}] Using partial heuristic result as fallback ({AnswerCount} answers)",
                    submissionId, heuristicResult.Count);
                
                // Fill missing questions with "[Answer not found]" marker instead of entire text
                foreach (var q in questions)
                {
                    if (!heuristicResult.ContainsKey(q.QuestionNumber))
                    {
                        heuristicResult[q.QuestionNumber] = "[Answer not found - Question marker not detected in OCR text]";
                    }
                }
                return heuristicResult;
            }

            _logger.LogWarning(
                "[{SubmissionId}] All segmentation methods failed, marking all as not found",
                submissionId);
            return questions.ToDictionary(q => q.QuestionNumber, _ => "[Answer not found - Segmentation failed]");
        }

        /// <summary>
        /// Attempts to segment answers using pattern-based heuristics (no AI call needed).
        /// Looks for common question markers: Q1, Question 1, 1), 1., Ans 1, etc.
        /// Includes fuzzy matching for OCR errors (Q→8, Q→0, Q→O, etc.)
        /// </summary>
        private Dictionary<int, string>? TryHeuristicSegmentation(
            string extractedText,
            List<ExamQuestionWithRubric> questions,
            Guid submissionId)
        {
            if (string.IsNullOrWhiteSpace(extractedText))
            {
                return null;
            }

            // Preprocess text to fix common OCR errors
            extractedText = PreprocessOcrText(extractedText);

            var result = new Dictionary<int, string>();
            var questionNumbers = questions.Select(q => q.QuestionNumber).OrderBy(n => n).ToList();

            // Pattern to match question markers with OCR error tolerance:
            // - Standard: Q1, Q.1, Q-1, Question 1, 1), 1., Ans 1, Answer 1
            // - OCR errors: 8-1 (Q→8), 0-1 (Q→0), O-1 (Q→O), 100-1 (Q→10/1)
            // (?i) = case insensitive
            var questionPattern = @"(?i)(?:^|\n)\s*(?:Q\.?\s*|Question\s+|Ans(?:wer)?\s+|[8O0][-\s]?)?(\d+)[\)\.:\-]?\s*";
            var matches = Regex.Matches(extractedText, questionPattern);

            if (matches.Count == 0)
            {
                _logger.LogDebug(
                    "[{SubmissionId}] No question markers found in OCR text",
                    submissionId);
                return null;
            }

            _logger.LogDebug(
                "[{SubmissionId}] Found {MatchCount} potential question markers",
                submissionId, matches.Count);

            // Group matches by question number
            var questionPositions = new List<(int QuestionNumber, int Position)>();
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out int qNum))
                {
                    // Only consider question numbers that exist in our exam
                    if (questionNumbers.Contains(qNum))
                    {
                        questionPositions.Add((qNum, match.Index));
                    }
                }
            }

            if (questionPositions.Count == 0)
            {
                return null;
            }

            // Sort by position in text
            questionPositions = questionPositions.OrderBy(p => p.Position).ToList();

            // Extract text between question markers
            for (int i = 0; i < questionPositions.Count; i++)
            {
                var (qNum, startPos) = questionPositions[i];
                var endPos = (i < questionPositions.Count - 1)
                    ? questionPositions[i + 1].Position
                    : extractedText.Length;

                var answerText = extractedText.Substring(startPos, endPos - startPos).Trim();
                
                // Remove the question marker from the beginning of the answer
                answerText = Regex.Replace(answerText, @"^(?i)(?:Q\.?\s*|Question\s+|Ans(?:wer)?\s+)?\d+[\)\.:]?\s*", "");
                
                result[qNum] = answerText;
            }

            _logger.LogInformation(
                "[{SubmissionId}] Heuristic segmentation extracted {AnswerCount}/{QuestionCount} answers",
                submissionId, result.Count, questions.Count);

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Preprocesses OCR extracted text to fix common OCR errors.
        /// Helps improve question marker detection by normalizing common mistakes.
        /// </summary>
        private string PreprocessOcrText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Common OCR errors and their fixes:
            // - "8-16" or "8-16=" → "Q-16" (Q misread as 8)
            // - "0-16" → "Q-16" (Q misread as 0)
            // - "O-16" → "Q-16" (Q misread as O)
            // - "100-18" → "Q-18" (Q misread as 10)
            // - "He11o" → "Hello" (ll misread as 11)
            // - "0neP1us" → "OnePlus" (O→0, l→1)

            var normalized = text;

            // Fix patterns like "8-16", "0-16", "O-16" at word boundaries or start of line
            // These are likely "Q-16" misread by OCR
            normalized = Regex.Replace(normalized, @"(?<=^|\s|\n)8-(\d{1,2})(?=[^\d]|$)", "Q-$1");
            normalized = Regex.Replace(normalized, @"(?<=^|\s|\n)0-(\d{1,2})(?=[^\d]|$)", "Q-$1");
            normalized = Regex.Replace(normalized, @"(?<=^|\s|\n)O-(\d{1,2})(?=[^\d]|$)", "Q-$1");
            
            // Fix patterns like "100-18" → "Q-18" (misread as number)
            normalized = Regex.Replace(normalized, @"(?<=^|\s|\n)10{1,2}-(\d{1,2})(?=[^\d]|$)", "Q-$1");
            normalized = Regex.Replace(normalized, @"(?<=^|\s|\n)1{2,3}-(\d{1,2})(?=[^\d]|$)", "Q-$1");

            // Fix patterns without hyphen: "8 16", "0 16", "816" if followed by typical answer patterns
            normalized = Regex.Replace(normalized, @"(?<=^|\n)\s*8\s?(\d{1,2})\s*[:\.\)]\s*", "Q-$1: ");
            normalized = Regex.Replace(normalized, @"(?<=^|\n)\s*0\s?(\d{1,2})\s*[:\.\)]\s*", "Q-$1: ");

            return normalized;
        }

        /// <summary>
        /// Builds the segmentation prompt.
        /// </summary>
        private static string BuildSegmentationPrompt(
            string extractedText,
            List<ExamQuestionWithRubric> questions)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyze the following OCR-extracted text from a student's handwritten exam answer sheet.");
            sb.AppendLine("Your task: Identify where each question's answer starts and ends.");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT INSTRUCTIONS:");
            sb.AppendLine("1. Look for question markers like: 'Q1', 'Question 1', '1)', '1.', 'Ans 1', 'Answer 1', or just '1'");
            sb.AppendLine("2. OCR text may have errors - be flexible with spacing, capitalization, and formatting");
            sb.AppendLine("3. Students may not answer questions in order - check entire text for each question");
            sb.AppendLine("4. If a question marker is found but no text follows, return '[Not answered]'");
            sb.AppendLine("5. If no marker is found for a question, search for text matching the question topic");
            sb.AppendLine();
            sb.AppendLine("EXAM QUESTIONS:");
            foreach (var q in questions.OrderBy(q => q.QuestionNumber))
            {
                sb.AppendLine($"Q{q.QuestionNumber}: {q.QuestionText.Substring(0, Math.Min(100, q.QuestionText.Length))}...");
            }
            sb.AppendLine();
            sb.AppendLine("OCR EXTRACTED TEXT:");
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine(extractedText);
            sb.AppendLine("═══════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine("Return ONLY a JSON object mapping question numbers to student answers.");
            sb.AppendLine("Format: { \"Q1\": \"student's answer text...\", \"Q2\": \"student's answer text...\", ... }");
            sb.AppendLine("Use question numbers that match the exam (" + 
                string.Join(", ", questions.OrderBy(q => q.QuestionNumber).Select(q => $"Q{q.QuestionNumber}")) + ")");
            sb.AppendLine();
            sb.AppendLine("EXAMPLES:");
            sb.AppendLine("Good: { \"Q1\": \"Photosynthesis is...\", \"Q2\": \"The three states...\", \"Q3\": \"[Not answered]\" }");
            sb.AppendLine("Bad: Mixing question numbers, missing quotes, adding comments");

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
