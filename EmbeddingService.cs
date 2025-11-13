using Azure;
using Azure.AI.OpenAI;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SmartStudyFunc
{
    public static class EmbeddingServiceHelper
    {
        private static OpenAIClient _client;
        private static string _deploymentName;

        public static void Initialize(string endpoint, string apiKey, string deploymentName)
        {
            if (string.IsNullOrWhiteSpace(endpoint) ||
                string.IsNullOrWhiteSpace(apiKey) ||
                string.IsNullOrWhiteSpace(deploymentName))
            {
                throw new ArgumentException("Azure OpenAI settings are missing.");
            }

            _client = new OpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
            _deploymentName = deploymentName;
        }

        public static async Task<byte[]> CreateEmbeddingAsync(string text)
        {
            if (_client == null)
                throw new InvalidOperationException("EmbeddingService not initialized.");

            if (string.IsNullOrWhiteSpace(text))
                text = ".";

            try
            {
                var response = await _client.GetEmbeddingsAsync(
                    new EmbeddingsOptions(_deploymentName, new[] { text }));

                var vector = response.Value.Data[0].Embedding.ToArray(); // float[]

                // Convert float[] to byte[]
                byte[] bytes = new byte[vector.Length * sizeof(float)];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);

                return bytes;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error generating embeddings: {ex.Message}", ex);
            }
        }
    }
}
