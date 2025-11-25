using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SmartStudyFunc.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartStudyFunc.Functions
{
    public class BatchEvaluate
    {
        private readonly ILogger<BatchEvaluate> _logger;
        private readonly EvaluateAnswer _evaluateAnswerFunction;
        private const int MaxConcurrency = 3;

        public BatchEvaluate(
            ILogger<BatchEvaluate> logger,
            EvaluateAnswer evaluateAnswerFunction)
        {
            _logger = logger;
            _evaluateAnswerFunction = evaluateAnswerFunction;
        }

        [Function("BatchEvaluate")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "answers/evaluate/batch")] HttpRequest req,
            CancellationToken ct)
        {
            _logger.LogInformation("BatchEvaluate function triggered");

            try
            {
                // Parse request body
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    return new BadRequestObjectResult(new { Error = "Request body is required" });
                }

                BatchEvaluateRequest? request;
                try
                {
                    request = JsonConvert.DeserializeObject<BatchEvaluateRequest>(requestBody);
                }
                catch (JsonException ex)
                {
                    return new BadRequestObjectResult(new { Error = "Invalid JSON format", Details = ex.Message });
                }

                if (request == null || request.Evaluations == null || request.Evaluations.Count == 0)
                {
                    return new BadRequestObjectResult(new { Error = "Evaluations array is required and must not be empty" });
                }

                _logger.LogInformation("Processing batch of {Count} evaluations", request.Evaluations.Count);

                // Limit concurrency to avoid overwhelming resources
                using var semaphore = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);
                var tasks = new List<Task<EvaluateAnswerResponse>>();

                foreach (var evaluation in request.Evaluations)
                {
                    await semaphore.WaitAsync(ct);

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            return await EvaluateSingleAsync(evaluation, ct);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, ct);

                    tasks.Add(task);
                }

                // Wait for all evaluations to complete
                var results = await Task.WhenAll(tasks);

                _logger.LogInformation("Batch evaluation complete: {Success} successful, {Failed} failed",
                    results.Count(r => r.Success), results.Count(r => !r.Success));

                // Build response
                var response = new BatchEvaluateResponse
                {
                    Success = true,
                    TotalRequested = request.Evaluations.Count,
                    TotalProcessed = results.Length,
                    Results = results.ToList()
                };

                return new OkObjectResult(response);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Batch evaluation was cancelled");
                return new StatusCodeResult(499); // Client Closed Request
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch evaluation failed");
                return new ObjectResult(new { Error = "Batch evaluation failed", Details = ex.Message })
                {
                    StatusCode = 500
                };
            }
        }

        private async Task<EvaluateAnswerResponse> EvaluateSingleAsync(
            EvaluateAnswerRequest evaluation,
            CancellationToken ct)
        {
            try
            {
                // Create a mock HttpRequest for the single evaluation function
                var context = new DefaultHttpContext();
                var request = context.Request;
                request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(evaluation)));
                request.ContentType = "application/json";

                // Call EvaluateAnswer function
                var result = await _evaluateAnswerFunction.Run(request, ct);

                if (result is OkObjectResult okResult && okResult.Value is EvaluateAnswerResponse response)
                {
                    return response;
                }
                else if (result is ObjectResult errorResult)
                {
                    return new EvaluateAnswerResponse
                    {
                        Success = false,
                        Error = $"Evaluation failed: {errorResult.Value}"
                    };
                }
                else
                {
                    return new EvaluateAnswerResponse
                    {
                        Success = false,
                        Error = "Unknown evaluation error"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Single evaluation failed for QuestionId={QuestionId}", evaluation.QuestionId);
                return new EvaluateAnswerResponse
                {
                    Success = false,
                    Error = $"Exception: {ex.Message}"
                };
            }
        }
    }
}
