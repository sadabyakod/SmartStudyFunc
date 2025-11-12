using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Validation result for Azure OpenAI deployment configuration
    /// </summary>
    public enum ValidationStatus
    {
        Success,
        NotConfigured,
        DeploymentNotFound,
        Unauthorized,
        TransientError,
        UnknownError
    }

    /// <summary>
    /// Result of deployment validation
    /// </summary>
    public class ValidationResult
    {
        public ValidationStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public int? EmbeddingDimensions { get; set; }
        public string? DeploymentName { get; set; }
    }

    /// <summary>
    /// Service for creating embeddings using Azure OpenAI or fake embeddings as fallback.
    /// </summary>
    public class EmbeddingService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmbeddingService> _logger;
        private bool _useRealEmbeddings;
        private OpenAIClient? _openAiClient;
        private string? _embeddingDeployment;
        private string? _endpoint;
        private string? _chatDeployment;

        public EmbeddingService(IConfiguration configuration, ILogger<EmbeddingService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var useRealSetting = _configuration["USE_REAL_EMBEDDINGS"];
            _useRealEmbeddings = !string.IsNullOrEmpty(useRealSetting) && 
                                 useRealSetting.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (_useRealEmbeddings)
            {
                InitializeAzureOpenAI();
            }
            else
            {
                _logger.LogInformation("EmbeddingService: Using fake embeddings (set USE_REAL_EMBEDDINGS=true for real)");
            }
        }

        private void InitializeAzureOpenAI()
        {
            try
            {
                _endpoint = _configuration["AzureOpenAI:Endpoint"];
                
                if (string.IsNullOrWhiteSpace(_endpoint))
                {
                    _logger.LogError(
                        "Azure OpenAI Endpoint is missing. " +
                        "Please set 'AzureOpenAI:Endpoint' in local.settings.json or run tools/set-azureopenai-config.ps1");
                    throw new InvalidOperationException("Missing configuration: AzureOpenAI:Endpoint");
                }
                
                var apiKey = _configuration["AzureOpenAI:ApiKey"];
                
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    _logger.LogError(
                        "Azure OpenAI API Key is missing. " +
                        "Please set 'AzureOpenAI:ApiKey' in local.settings.json or run tools/set-azureopenai-config.ps1. " +
                        "Get your key from Azure Portal → Your OpenAI Resource → Keys and Endpoint");
                    throw new InvalidOperationException("Missing configuration: AzureOpenAI:ApiKey");
                }
                
                _embeddingDeployment = _configuration["AzureOpenAI:EmbeddingDeployment"];
                
                if (string.IsNullOrWhiteSpace(_embeddingDeployment))
                {
                    _logger.LogWarning(
                        "AzureOpenAI:EmbeddingDeployment not configured, using default: text-embedding-3-large");
                    _embeddingDeployment = "text-embedding-3-large";
                }

                _chatDeployment = _configuration["AzureOpenAI:ChatDeployment"];
                
                if (string.IsNullOrWhiteSpace(_chatDeployment))
                {
                    _logger.LogWarning(
                        "AzureOpenAI:ChatDeployment not configured, using default: gpt-4o-mini");
                    _chatDeployment = "gpt-4o-mini";
                }

                _openAiClient = new OpenAIClient(new Uri(_endpoint), new AzureKeyCredential(apiKey));

                _logger.LogInformation(
                    "EmbeddingService: Initialized with Azure OpenAI. Endpoint={Endpoint}, EmbeddingModel={EmbeddingModel}, ChatModel={ChatModel}",
                    _endpoint, _embeddingDeployment, _chatDeployment);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Failed to initialize Azure OpenAI service, falling back to fake embeddings. " +
                    "Run tools/set-azureopenai-config.ps1 to configure Azure OpenAI settings.");
                _useRealEmbeddings = false;
                _openAiClient = null;
            }
        }

        /// <summary>
        /// Creates an embedding for the given text.
        /// Uses Azure OpenAI if configured, otherwise falls back to fake embeddings.
        /// </summary>
        /// <param name="text">Text to create embedding for</param>
        /// <returns>Byte array representation of the embedding vector</returns>
        public async Task<byte[]> CreateEmbedding(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text cannot be null or empty", nameof(text));
            }

            // Try real embeddings if enabled
            if (_useRealEmbeddings && _openAiClient != null && !string.IsNullOrEmpty(_embeddingDeployment))
            {
                try
                {
                    return await CreateRealEmbedding(text);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get real embedding, falling back to fake: {Message}", ex.Message);
                    // Fall through to fake embeddings
                }
            }

            // Fallback to fake embeddings
            return await CreateFakeEmbedding(text);
        }

        private async Task<byte[]> CreateRealEmbedding(string text)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogDebug("Requesting embedding from Azure OpenAI (attempt {Attempt}/{Max})", attempt, maxRetries);

                    var options = new EmbeddingsOptions(_embeddingDeployment!, new[] { text });
                    Response<Embeddings> response = await _openAiClient!.GetEmbeddingsAsync(options);

                    if (response.Value.Data.Count == 0)
                    {
                        throw new InvalidOperationException("No embedding data returned from Azure OpenAI");
                    }

                    var embeddingItem = response.Value.Data[0];
                    var floatArray = embeddingItem.Embedding.ToArray();

                    _logger.LogDebug("Received embedding with {Dimensions} dimensions", floatArray.Length);

                    // Convert float[] to byte[]
                    var byteArray = new byte[floatArray.Length * sizeof(float)];
                    Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);

                    return byteArray;
                }
                catch (RequestFailedException ex) when (IsTransientError(ex) && attempt < maxRetries)
                {
                    _logger.LogWarning(
                        "Transient error on embedding request (attempt {Attempt}/{Max}): {Message}",
                        attempt, maxRetries, ex.Message);
                    await Task.Delay(retryDelayMs * attempt);
                }
            }

            throw new InvalidOperationException($"Failed to get embedding after {maxRetries} attempts");
        }

        private static Task<byte[]> CreateFakeEmbedding(string text)
        {
            // Deterministic fake embedding based on text hash
            var random = new Random(text.GetHashCode());
            var vec = new float[1536]; // Standard embedding dimension

            for (int i = 0; i < vec.Length; i++)
            {
                vec[i] = (float)(random.NextDouble() * 2 - 1);
            }

            var bytes = new byte[vec.Length * sizeof(float)];
            Buffer.BlockCopy(vec, 0, bytes, 0, bytes.Length);

            return Task.FromResult(bytes);
        }

        private static bool IsTransientError(RequestFailedException ex)
        {
            return ex.Status == 429 || 
                   ex.Status >= 500 || 
                   ex.ErrorCode == "Timeout" ||
                   ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validates Azure OpenAI deployment configuration by attempting a small embedding request.
        /// This helps verify that the endpoint, API key, and deployment name are all correct.
        /// </summary>
        /// <returns>Validation result with status and helpful error messages</returns>
        public async Task<ValidationResult> ValidateDeploymentsAsync()
        {
            // Check if real embeddings are enabled
            if (!_useRealEmbeddings)
            {
                return new ValidationResult
                {
                    Status = ValidationStatus.NotConfigured,
                    Message = "Real embeddings not enabled. Set USE_REAL_EMBEDDINGS=true in local.settings.json"
                };
            }

            // Check if client was initialized
            if (_openAiClient == null || string.IsNullOrWhiteSpace(_embeddingDeployment))
            {
                return new ValidationResult
                {
                    Status = ValidationStatus.NotConfigured,
                    Message = "Azure OpenAI client not initialized. Check that Endpoint and ApiKey are configured correctly. " +
                             "Run tools/set-azureopenai-config.ps1 to configure."
                };
            }

            const int maxRetries = 3;
            const int retryDelayMs = 1000;
            Exception? lastException = null;

            // Attempt validation with retry logic for transient errors
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    _logger.LogInformation(
                        "Validating Azure OpenAI deployment '{Deployment}' (attempt {Attempt}/{Max})...", 
                        _embeddingDeployment, attempt, maxRetries);

                    // Make a small test embedding request
                    var options = new EmbeddingsOptions(_embeddingDeployment, new[] { "validate" });
                    Response<Embeddings> response = await _openAiClient.GetEmbeddingsAsync(options);

                    if (response.Value.Data.Count == 0)
                    {
                        return new ValidationResult
                        {
                            Status = ValidationStatus.UnknownError,
                            Message = "Validation request succeeded but no embedding data was returned"
                        };
                    }

                    var embeddingItem = response.Value.Data[0];
                    var dimensions = embeddingItem.Embedding.Length;

                    _logger.LogInformation(
                        "✓ Validation successful! Deployment '{Deployment}' is working. Embedding dimensions: {Dimensions}",
                        _embeddingDeployment, dimensions);

                    return new ValidationResult
                    {
                        Status = ValidationStatus.Success,
                        Message = $"Successfully validated deployment '{_embeddingDeployment}'. Endpoint: {_endpoint}",
                        EmbeddingDimensions = dimensions,
                        DeploymentName = _embeddingDeployment
                    };
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Deployment not found - this is a configuration error, not transient
                    _logger.LogError(
                        "Deployment '{Deployment}' not found (HTTP 404). " +
                        "Please verify the deployment name matches exactly in Azure AI Studio. " +
                        "Go to: Azure Portal → Your OpenAI Resource → Model deployments",
                        _embeddingDeployment);

                    return new ValidationResult
                    {
                        Status = ValidationStatus.DeploymentNotFound,
                        Message = $"Deployment '{_embeddingDeployment}' not found. " +
                                 $"Verify the deployment name in Azure AI Studio matches exactly (case-sensitive). " +
                                 $"Common deployments: 'text-embedding-3-large', 'text-embedding-ada-002'",
                        DeploymentName = _embeddingDeployment
                    };
                }
                catch (RequestFailedException ex) when (ex.Status == 401 || ex.Status == 403)
                {
                    // Unauthorized - API key issue
                    _logger.LogError(
                        "Authentication failed (HTTP {Status}). " +
                        "Please verify your API key is correct. " +
                        "Get your key from: Azure Portal → Your OpenAI Resource → Keys and Endpoint",
                        ex.Status);

                    return new ValidationResult
                    {
                        Status = ValidationStatus.Unauthorized,
                        Message = $"Authentication failed (HTTP {ex.Status}). " +
                                 $"Verify your API key in local.settings.json is correct. " +
                                 $"You may need to regenerate the key in Azure Portal."
                    };
                }
                catch (RequestFailedException ex) when (IsTransientError(ex) && attempt < maxRetries)
                {
                    // Transient error - retry
                    lastException = ex;
                    _logger.LogWarning(
                        "Transient error during validation (attempt {Attempt}/{Max}): HTTP {Status} - {Message}",
                        attempt, maxRetries, ex.Status, ex.Message);
                    await Task.Delay(retryDelayMs * attempt);
                }
                catch (Exception ex)
                {
                    // Other unexpected errors
                    lastException = ex;
                    _logger.LogError(ex, 
                        "Unexpected error during validation (attempt {Attempt}/{Max}): {Message}",
                        attempt, maxRetries, ex.Message);

                    if (attempt >= maxRetries)
                    {
                        return new ValidationResult
                        {
                            Status = ValidationStatus.UnknownError,
                            Message = $"Validation failed with error: {ex.Message}"
                        };
                    }

                    await Task.Delay(retryDelayMs * attempt);
                }
            }

            // All retries exhausted for transient errors
            return new ValidationResult
            {
                Status = ValidationStatus.TransientError,
                Message = $"Validation failed after {maxRetries} attempts due to transient errors. " +
                         $"Last error: {lastException?.Message ?? "Unknown"}. " +
                         $"This may be due to rate limiting or temporary service issues."
            };
        }
    }
}
