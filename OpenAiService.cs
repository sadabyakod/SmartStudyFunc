using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmartStudyFunc
{
    /// <summary>
    /// Azure OpenAI service for embeddings and chat completions.
    /// Required NuGet Package: Azure.AI.OpenAI (version 1.0.0-beta.12 or later)
    /// Install via: dotnet add package Azure.AI.OpenAI
    /// </summary>
    public class OpenAiService
    {
        private readonly OpenAIClient _client;
        private readonly string _embeddingDeployment;
        private readonly string _chatDeployment;
        private readonly ILogger? _logger;
        private const int MaxRetries = 3;
        private const int RetryDelayMs = 1000;

        public OpenAiService(IConfiguration configuration, ILogger? logger = null)
        {
            _logger = logger;

            // Read configuration - support both Key formats (Key and ApiKey)
            var endpoint = configuration["AzureOpenAI:Endpoint"] 
                ?? throw new ArgumentException("Missing configuration: AzureOpenAI:Endpoint");
            var apiKey = configuration["AzureOpenAI:ApiKey"] 
                ?? configuration["AzureOpenAI:Key"]
                ?? throw new ArgumentException("Missing configuration: AzureOpenAI:ApiKey or AzureOpenAI:Key");
            _embeddingDeployment = configuration["AzureOpenAI:EmbeddingDeployment"]
                ?? configuration["AzureOpenAI:DeploymentEmbedding"]
                ?? throw new ArgumentException("Missing configuration: AzureOpenAI:EmbeddingDeployment");
            _chatDeployment = configuration["AzureOpenAI:ChatDeployment"] 
                ?? throw new ArgumentException("Missing configuration: AzureOpenAI:ChatDeployment");

            // Initialize Azure OpenAI client
            _client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

            _logger?.LogInformation(
                "OpenAI Service initialized - Endpoint={Endpoint}, EmbeddingDeployment={EmbeddingModel}, ChatDeployment={ChatModel}",
                endpoint, _embeddingDeployment, _chatDeployment
            );
            
            // Debug log to verify configuration is loaded
            _logger?.LogDebug("Configuration check - AzureOpenAI:Endpoint exists: {EndpointExists}, AzureOpenAI:ChatDeployment exists: {ChatExists}",
                !string.IsNullOrEmpty(endpoint), !string.IsNullOrEmpty(_chatDeployment));
        }

        /// <summary>
        /// Gets embedding vector as byte array for the given input text.
        /// </summary>
        /// <param name="input">Text to embed</param>
        /// <returns>Byte array representation of float[] embedding</returns>
        public async Task<byte[]> GetEmbeddingBytesAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("Input text cannot be null or empty", nameof(input));
            }

            Exception? lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    _logger?.LogDebug("Requesting embedding (attempt {Attempt}/{MaxRetries})", attempt, MaxRetries);

                    var options = new EmbeddingsOptions(_embeddingDeployment, new[] { input });
                    Response<Embeddings> response = await _client.GetEmbeddingsAsync(options);

                    if (response.Value.Data.Count == 0)
                    {
                        throw new InvalidOperationException("No embedding data returned from Azure OpenAI");
                    }

                    // Get the embedding vector
                    var embeddingItem = response.Value.Data[0];
                    var floatArray = embeddingItem.Embedding.ToArray();

                    _logger?.LogDebug("Received embedding with {Dimensions} dimensions", floatArray.Length);

                    // Convert float[] to byte[]
                    var byteArray = new byte[floatArray.Length * sizeof(float)];
                    Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);

                    return byteArray;
                }
                catch (RequestFailedException ex) when (IsTransientError(ex) && attempt < MaxRetries)
                {
                    lastException = ex;
                    _logger?.LogWarning(
                        ex,
                        "Transient error on embedding request (attempt {Attempt}/{MaxRetries}): {Message}",
                        attempt, MaxRetries, ex.Message
                    );
                    await Task.Delay(RetryDelayMs * attempt);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to get embedding: {Message}", ex.Message);
                    throw new InvalidOperationException(
                        $"Failed to get embedding from Azure OpenAI: {ex.Message}", ex
                    );
                }
            }

            throw new InvalidOperationException(
                $"Failed to get embedding after {MaxRetries} attempts: {lastException?.Message}",
                lastException
            );
        }

        /// <summary>
        /// Gets chat completion response for the given prompt.
        /// </summary>
        /// <param name="prompt">User prompt</param>
        /// <param name="systemMessages">Optional system messages to set context</param>
        /// <returns>Chat completion content as string</returns>
        public async Task<string> GetChatCompletionAsync(string prompt, IEnumerable<string>? systemMessages = null)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));
            }

            Exception? lastException = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    _logger?.LogDebug("Requesting chat completion (attempt {Attempt}/{MaxRetries})", attempt, MaxRetries);

                    var chatMessages = new List<ChatRequestMessage>();

                    // Add system messages if provided
                    if (systemMessages != null)
                    {
                        foreach (var sysMsg in systemMessages)
                        {
                            if (!string.IsNullOrWhiteSpace(sysMsg))
                            {
                                chatMessages.Add(new ChatRequestSystemMessage(sysMsg));
                            }
                        }
                    }

                    // Add user prompt
                    chatMessages.Add(new ChatRequestUserMessage(prompt));

                    var options = new ChatCompletionsOptions(_chatDeployment, chatMessages)
                    {
                        Temperature = 0.7f,
                        MaxTokens = 800,
                        NucleusSamplingFactor = 0.95f,
                        FrequencyPenalty = 0,
                        PresencePenalty = 0
                    };

                    Response<ChatCompletions> response = await _client.GetChatCompletionsAsync(options);

                    if (response.Value.Choices.Count == 0)
                    {
                        throw new InvalidOperationException("No chat completion choices returned from Azure OpenAI");
                    }

                    var content = response.Value.Choices[0].Message.Content;
                    
                    _logger?.LogDebug(
                        "Chat completion received: {Tokens} tokens used",
                        response.Value.Usage.TotalTokens
                    );

                    return content ?? string.Empty;
                }
                catch (RequestFailedException ex) when (IsTransientError(ex) && attempt < MaxRetries)
                {
                    lastException = ex;
                    _logger?.LogWarning(
                        ex,
                        "Transient error on chat completion request (attempt {Attempt}/{MaxRetries}): Status={Status}, ErrorCode={ErrorCode}, Message={Message}",
                        attempt, MaxRetries, ex.Status, ex.ErrorCode, ex.Message
                    );
                    await Task.Delay(RetryDelayMs * attempt);
                }
                catch (Exception ex)
                {
                    var errorDetails = ex is RequestFailedException rfe 
                        ? $"Status={rfe.Status}, ErrorCode={rfe.ErrorCode}" 
                        : ex.GetType().Name;
                    _logger?.LogError(ex, "Failed to get chat completion: {ErrorDetails}, Message={Message}, Deployment={Deployment}", 
                        errorDetails, ex.Message, _chatDeployment);
                    throw new InvalidOperationException(
                        $"Failed to get chat completion from Azure OpenAI (deployment: {_chatDeployment}): {ex.Message}", ex
                    );
                }
            }

            throw new InvalidOperationException(
                $"Failed to get chat completion after {MaxRetries} attempts: {lastException?.Message}",
                lastException
            );
        }

        /// <summary>
        /// Determines if an exception is a transient error that can be retried.
        /// </summary>
        private static bool IsTransientError(RequestFailedException ex)
        {
            // Retry on rate limiting (429), server errors (5xx), and timeout errors
            return ex.Status == 429 
                || ex.Status >= 500 
                || ex.ErrorCode == "Timeout" 
                || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }
    }
}
