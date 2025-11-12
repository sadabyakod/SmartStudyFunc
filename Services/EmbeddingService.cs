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
    /// Service for creating embeddings using Azure OpenAI or fake embeddings as fallback.
    /// </summary>
    public class EmbeddingService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmbeddingService> _logger;
        private bool _useRealEmbeddings;
        private OpenAIClient? _openAiClient;
        private string? _embeddingDeployment;

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
                var endpoint = _configuration["AzureOpenAI:Endpoint"] 
                    ?? throw new InvalidOperationException("Missing configuration: AzureOpenAI:Endpoint");
                
                var apiKey = _configuration["AzureOpenAI:ApiKey"] 
                    ?? _configuration["AzureOpenAI:Key"]
                    ?? throw new InvalidOperationException("Missing configuration: AzureOpenAI:ApiKey");
                
                _embeddingDeployment = _configuration["AzureOpenAI:EmbeddingDeployment"]
                    ?? _configuration["AzureOpenAI:DeploymentEmbedding"]
                    ?? "text-embedding-3-large";

                _openAiClient = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));

                _logger.LogInformation(
                    "EmbeddingService: Initialized with Azure OpenAI. Endpoint={Endpoint}, Model={Model}",
                    endpoint, _embeddingDeployment);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize Azure OpenAI service, falling back to fake embeddings");
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
    }
}
