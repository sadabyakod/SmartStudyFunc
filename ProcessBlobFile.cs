using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs.Models;
using System;
using System.IO;

namespace SmartStudyFunc
{
    public class ProcessBlobFile
    {
        private readonly ILogger<ProcessBlobFile> _logger;

        public ProcessBlobFile(ILogger<ProcessBlobFile> logger)
        {
            _logger = logger;
        }

        [Function(nameof(ProcessBlobFile))]
        public void Run(
            [BlobTrigger("textbooks/{name}", Connection = "AzureWebJobsStorage")] Stream blobStream,
            string name,
            Uri uri,
            BlobProperties properties)
        {
            try
            {
                long fileSizeBytes = blobStream.Length;
                double fileSizeKB = fileSizeBytes / 1024.0;
                double fileSizeMB = fileSizeKB / 1024.0;
                string fileExtension = Path.GetExtension(name);

                _logger.LogInformation("File Uploaded: {FileName} | Size: {Bytes:N0} bytes ({MB:N2} MB) | Extension: {Extension}", 
                    name, fileSizeBytes, fileSizeMB, string.IsNullOrEmpty(fileExtension) ? "none" : fileExtension);

                // Add custom file processing logic here

                _logger.LogInformation("Processing completed: {FileName}", name);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error processing {FileName}: {Message}", name, ex.Message);
            }
        }
    }
}
