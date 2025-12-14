using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;
using SmartStudyFunc.Services;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// Timer-triggered function for cleaning up old blob files based on retention policy.
    /// Runs daily at 2:00 AM UTC.
    /// </summary>
    public class CleanupOldWrittenSubmissions
    {
        private readonly IWrittenSubmissionRepository _repository;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<CleanupOldWrittenSubmissions> _logger;
        private readonly int _retentionDays;

        public CleanupOldWrittenSubmissions(
            IWrittenSubmissionRepository repository,
            BlobServiceClient blobServiceClient,
            IConfiguration configuration,
            ILogger<CleanupOldWrittenSubmissions> logger)
        {
            _repository = repository;
            _blobServiceClient = blobServiceClient;
            _logger = logger;
            _retentionDays = configuration.GetValue<int>("WrittenSubmission:RetentionDays", 30);
        }

        /// <summary>
        /// Cleanup function that runs daily at 2:00 AM UTC.
        /// Deletes blob files older than the configured retention period.
        /// </summary>
        [Function(nameof(CleanupOldWrittenSubmissions))]
        public async Task Run(
            [TimerTrigger("0 0 2 * * *")] TimerInfo timerInfo,
            CancellationToken cancellationToken)
        {
            var runId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation(
                "[Cleanup:{RunId}] Starting cleanup job. Retention period: {RetentionDays} days. Last run: {LastRun}",
                runId, _retentionDays, timerInfo.ScheduleStatus?.Last);

            var totalDeleted = 0;
            var totalFailed = 0;
            var totalBlobsDeleted = 0;

            try
            {
                // Get old submissions eligible for cleanup
                var oldSubmissions = await _repository.GetOldSubmissionsAsync(
                    _retentionDays, cancellationToken);

                _logger.LogInformation(
                    "[Cleanup:{RunId}] Found {Count} submissions eligible for blob cleanup",
                    runId, oldSubmissions.Count);

                foreach (var submission in oldSubmissions)
                {
                    try
                    {
                        var blobsDeleted = await DeleteSubmissionBlobsAsync(
                            submission, runId, cancellationToken);
                        
                        totalBlobsDeleted += blobsDeleted;

                        // Mark blobs as deleted in database
                        await _repository.MarkBlobsDeletedAsync(submission.Id, cancellationToken);
                        
                        totalDeleted++;

                        _logger.LogInformation(
                            "[Cleanup:{RunId}] Cleaned up submission {SubmissionId}: {BlobCount} blobs deleted",
                            runId, submission.Id, blobsDeleted);
                    }
                    catch (Exception ex)
                    {
                        totalFailed++;
                        _logger.LogError(ex,
                            "[Cleanup:{RunId}] Failed to cleanup submission {SubmissionId}",
                            runId, submission.Id);
                    }
                }

                _logger.LogInformation(
                    "[Cleanup:{RunId}] Cleanup completed. Submissions processed: {Processed}, Failed: {Failed}, Blobs deleted: {BlobsDeleted}",
                    runId, totalDeleted, totalFailed, totalBlobsDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Cleanup:{RunId}] Cleanup job failed with unexpected error",
                    runId);
                throw;
            }
        }

        private async Task<int> DeleteSubmissionBlobsAsync(
            WrittenSubmission submission,
            string runId,
            CancellationToken cancellationToken)
        {
            var deletedCount = 0;

            // Delete answer sheet blobs
            foreach (var blobPath in submission.FilePaths)
            {
                try
                {
                    var deleted = await DeleteBlobAsync(blobPath, cancellationToken);
                    if (deleted)
                    {
                        deletedCount++;
                        _logger.LogDebug(
                            "[Cleanup:{RunId}] Deleted blob: {BlobPath}",
                            runId, blobPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[Cleanup:{RunId}] Failed to delete blob: {BlobPath}",
                        runId, blobPath);
                }
            }

            // Delete extracted text blob if exists
            if (!string.IsNullOrWhiteSpace(submission.ExtractedTextBlobPath))
            {
                try
                {
                    var deleted = await DeleteBlobAsync(submission.ExtractedTextBlobPath, cancellationToken);
                    if (deleted)
                    {
                        deletedCount++;
                        _logger.LogDebug(
                            "[Cleanup:{RunId}] Deleted OCR text blob: {BlobPath}",
                            runId, submission.ExtractedTextBlobPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[Cleanup:{RunId}] Failed to delete OCR text blob: {BlobPath}",
                        runId, submission.ExtractedTextBlobPath);
                }
            }

            return deletedCount;
        }

        private async Task<bool> DeleteBlobAsync(string blobPath, CancellationToken cancellationToken)
        {
            // Parse container and blob name from path
            var parts = blobPath.Split('/', 2);
            
            string containerName;
            string blobName;

            if (parts.Length == 2)
            {
                containerName = parts[0];
                blobName = parts[1];
            }
            else
            {
                containerName = "written-answers";
                blobName = blobPath;
            }

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            var response = await blobClient.DeleteIfExistsAsync(
                Azure.Storage.Blobs.Models.DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken);

            return response.Value;
        }
    }
}
