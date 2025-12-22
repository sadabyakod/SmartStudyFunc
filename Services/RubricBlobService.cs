using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services
{
    /// <summary>
    /// Service for storing and retrieving question rubrics from Azure Blob Storage.
    /// Container: modalquestions-rubrics
    /// Path format: paper-{PaperId}/question-{QuestionId}.json
    /// </summary>
    public interface IRubricBlobService
    {
        /// <summary>
        /// Save a question rubric to blob storage
        /// </summary>
        Task<string> SaveRubricAsync(QuestionRubric rubric, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Save multiple rubrics for a paper
        /// </summary>
        Task<List<string>> SaveRubricsAsync(string paperId, List<QuestionRubric> rubrics, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Get a rubric by blob path
        /// </summary>
        Task<QuestionRubric?> GetRubricAsync(string blobPath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Get all rubrics for a paper
        /// </summary>
        Task<List<QuestionRubric>> GetRubricsForPaperAsync(string paperId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Delete all rubrics for a paper
        /// </summary>
        Task DeletePaperRubricsAsync(string paperId, CancellationToken cancellationToken = default);
    }

    public class RubricBlobService : IRubricBlobService
    {
        private const string ContainerName = "modalquestions-rubrics";
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<RubricBlobService> _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public RubricBlobService(
            BlobServiceClient blobServiceClient,
            ILogger<RubricBlobService> logger)
        {
            _blobServiceClient = blobServiceClient;
            _logger = logger;
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public async Task<string> SaveRubricAsync(QuestionRubric rubric, CancellationToken cancellationToken = default)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            // Path format: paper-{PaperId}/question-{QuestionId}.json
            var blobPath = $"paper-{rubric.PaperId}/question-{rubric.QuestionId}.json";
            var blobClient = containerClient.GetBlobClient(blobPath);

            var json = JsonSerializer.Serialize(rubric, _jsonOptions);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = "application/json"
                }
            };

            await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);

            _logger.LogInformation(
                "[RUBRIC_SAVED] PaperId={PaperId}, QuestionId={QuestionId}, BlobPath={BlobPath}",
                rubric.PaperId, rubric.QuestionId, blobPath);

            return $"{ContainerName}/{blobPath}";
        }

        public async Task<List<string>> SaveRubricsAsync(
            string paperId, 
            List<QuestionRubric> rubrics, 
            CancellationToken cancellationToken = default)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var blobPaths = new List<string>();

            foreach (var rubric in rubrics)
            {
                try
                {
                    rubric.PaperId = paperId; // Ensure paperId is set
                    var path = await SaveRubricAsync(rubric, cancellationToken);
                    blobPaths.Add(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[RUBRIC_SAVE_FAILED] PaperId={PaperId}, QuestionId={QuestionId}",
                        paperId, rubric.QuestionId);
                }
            }

            _logger.LogInformation(
                "[RUBRICS_SAVED] PaperId={PaperId}, Count={Count}",
                paperId, blobPaths.Count);

            return blobPaths;
        }

        public async Task<QuestionRubric?> GetRubricAsync(string blobPath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Handle full path or relative path
                var relativePath = blobPath;
                if (blobPath.StartsWith($"{ContainerName}/"))
                {
                    relativePath = blobPath.Substring(ContainerName.Length + 1);
                }

                var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
                var blobClient = containerClient.GetBlobClient(relativePath);

                if (!await blobClient.ExistsAsync(cancellationToken))
                {
                    _logger.LogWarning("[RUBRIC_NOT_FOUND] BlobPath={BlobPath}", blobPath);
                    return null;
                }

                var response = await blobClient.DownloadContentAsync(cancellationToken);
                var json = response.Value.Content.ToString();
                var rubric = JsonSerializer.Deserialize<QuestionRubric>(json, _jsonOptions);

                _logger.LogDebug("[RUBRIC_LOADED] BlobPath={BlobPath}", blobPath);
                return rubric;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RUBRIC_LOAD_FAILED] BlobPath={BlobPath}", blobPath);
                return null;
            }
        }

        public async Task<List<QuestionRubric>> GetRubricsForPaperAsync(
            string paperId, 
            CancellationToken cancellationToken = default)
        {
            var rubrics = new List<QuestionRubric>();

            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
                
                if (!await containerClient.ExistsAsync(cancellationToken))
                {
                    return rubrics;
                }

                var prefix = $"paper-{paperId}/";
                
                await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
                {
                    if (blobItem.Name.EndsWith(".json"))
                    {
                        var rubric = await GetRubricAsync(blobItem.Name, cancellationToken);
                        if (rubric != null)
                        {
                            rubrics.Add(rubric);
                        }
                    }
                }

                _logger.LogInformation(
                    "[RUBRICS_LOADED] PaperId={PaperId}, Count={Count}",
                    paperId, rubrics.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RUBRICS_LOAD_FAILED] PaperId={PaperId}", paperId);
            }

            return rubrics;
        }

        public async Task DeletePaperRubricsAsync(string paperId, CancellationToken cancellationToken = default)
        {
            try
            {
                var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
                
                if (!await containerClient.ExistsAsync(cancellationToken))
                {
                    return;
                }

                var prefix = $"paper-{paperId}/";
                var deletedCount = 0;

                await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
                {
                    var blobClient = containerClient.GetBlobClient(blobItem.Name);
                    await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                    deletedCount++;
                }

                _logger.LogInformation(
                    "[RUBRICS_DELETED] PaperId={PaperId}, Count={Count}",
                    paperId, deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RUBRICS_DELETE_FAILED] PaperId={PaperId}", paperId);
            }
        }
    }
}
