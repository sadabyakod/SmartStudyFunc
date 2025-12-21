using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// PRODUCTION-GRADE: Thread-safe syllabus cache for Azure Function
    /// Reduces Blob Storage calls and improves evaluation performance
    /// </summary>
    public class SyllabusCacheService
    {
        private readonly ILogger<SyllabusCacheService> _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IMemoryCache _cache;
        
        // Cache configuration
        private static readonly TimeSpan DefaultCacheExpiration = TimeSpan.FromHours(1);
        private static readonly TimeSpan SlidingExpiration = TimeSpan.FromMinutes(20);

        // In-memory cache for frequently accessed syllabus
        private readonly ConcurrentDictionary<string, (string Content, DateTime LoadedAt)> _syllabusCache;

        public SyllabusCacheService(
            ILogger<SyllabusCacheService> logger,
            BlobServiceClient blobServiceClient,
            IMemoryCache cache)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _syllabusCache = new ConcurrentDictionary<string, (string, DateTime)>();
        }

        /// <summary>
        /// Get syllabus content with automatic caching
        /// Thread-safe for Azure Functions scale-out
        /// </summary>
        public async Task<string> GetSyllabusContentAsync(
            SubjectCategory subject,
            int classLevel,
            string? customPath = null,
            CancellationToken cancellationToken = default)
        {
            // Build cache key
            var cacheKey = customPath ?? $"syllabus-{subject}-class{classLevel}";

            // Try memory cache first (fastest)
            if (_cache.TryGetValue<string>(cacheKey, out var cachedContent))
            {
                _logger.LogDebug("Syllabus cache HIT: {Key}", cacheKey);
                return cachedContent;
            }

            _logger.LogDebug("Syllabus cache MISS: {Key}, loading from Blob", cacheKey);

            // Load from Blob Storage
            var content = await LoadSyllabusFromBlobAsync(subject, classLevel, customPath, cancellationToken);

            // Cache the content
            if (!string.IsNullOrWhiteSpace(content))
            {
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = DefaultCacheExpiration,
                    SlidingExpiration = SlidingExpiration,
                    Size = content.Length // Track memory usage
                };

                _cache.Set(cacheKey, content, cacheOptions);
                _logger.LogInformation("Cached syllabus: {Key} ({Size} bytes)", cacheKey, content.Length);
            }

            return content;
        }

        /// <summary>
        /// Load syllabus from Azure Blob Storage
        /// SINGLE SOURCE OF TRUTH
        /// </summary>
        private async Task<string> LoadSyllabusFromBlobAsync(
            SubjectCategory subject,
            int classLevel,
            string? customPath,
            CancellationToken cancellationToken)
        {
            try
            {
                string blobPath;

                if (!string.IsNullOrWhiteSpace(customPath))
                {
                    blobPath = customPath;
                }
                else
                {
                    // Standard path: syllabus/class-{level}/{subject}.txt
                    blobPath = $"syllabus/class-{classLevel}/{subject.ToString().ToLowerInvariant()}.txt";
                }

                _logger.LogInformation("Loading syllabus from blob: {BlobPath}", blobPath);

                var containerClient = _blobServiceClient.GetBlobContainerClient("syllabus");
                var blobClient = containerClient.GetBlobClient(blobPath);

                if (!await blobClient.ExistsAsync(cancellationToken))
                {
                    _logger.LogWarning("Syllabus blob not found: {BlobPath}", blobPath);
                    return string.Empty;
                }

                var response = await blobClient.DownloadContentAsync(cancellationToken);
                var content = response.Value.Content.ToString();

                _logger.LogInformation("Loaded syllabus: {BlobPath} ({Size} bytes)", blobPath, content.Length);

                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load syllabus: {Subject} Class {Class}", subject, classLevel);
                return string.Empty;
            }
        }

        /// <summary>
        /// Pre-warm cache for common syllabus content
        /// Call this during Function startup or on a timer trigger
        /// </summary>
        public async Task PreWarmCacheAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Pre-warming syllabus cache...");

            // Common combinations for Classes 6-12
            var subjects = new[]
            {
                SubjectCategory.Mathematics,
                SubjectCategory.Physics,
                SubjectCategory.Chemistry,
                SubjectCategory.Biology,
                SubjectCategory.SocialScience
            };

            var classes = new[] { 6, 7, 8, 9, 10, 11, 12 };

            var tasks = new List<Task>();

            foreach (var subject in subjects)
            {
                foreach (var classLevel in classes)
                {
                    tasks.Add(GetSyllabusContentAsync(subject, classLevel, null, cancellationToken));
                }
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation("Syllabus cache pre-warmed successfully");
        }

        /// <summary>
        /// Clear cache (for testing or manual refresh)
        /// </summary>
        public void ClearCache()
        {
            _syllabusCache.Clear();
            _logger.LogInformation("Syllabus cache cleared");
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public SyllabusCacheStats GetCacheStats()
        {
            return new SyllabusCacheStats
            {
                CachedItemCount = _syllabusCache.Count,
                TotalMemoryBytes = _syllabusCache.Sum(kv => kv.Value.Content.Length)
            };
        }
    }

    /// <summary>
    /// Cache statistics for monitoring
    /// </summary>
    public class SyllabusCacheStats
    {
        public int CachedItemCount { get; set; }
        public long TotalMemoryBytes { get; set; }
    }
}
