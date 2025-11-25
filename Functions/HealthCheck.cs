using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SmartStudyFunc.Functions
{
    /// <summary>
    /// Lightweight health check endpoint for monitoring Function App availability and basic connectivity.
    /// Returns 200 OK with status information if healthy, 503 Service Unavailable if unhealthy.
    /// </summary>
    public class HealthCheck
    {
        private readonly ILogger<HealthCheck> _logger;
        private readonly IConfiguration _configuration;

        public HealthCheck(ILogger<HealthCheck> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [Function("HealthCheck")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
        {
            var startTime = DateTime.UtcNow;
            var checks = new System.Collections.Generic.Dictionary<string, object>();
            bool isHealthy = true;

            try
            {
                // 1. Basic Function App health
                checks["status"] = "running";
                checks["timestamp"] = DateTime.UtcNow;
                checks["uptime"] = GetUptime();
                checks["memory_mb"] = GetMemoryUsageMB();

                // 2. Configuration health
                try
                {
                    var sqlConnString = _configuration["ConnectionStrings:SqlDb"];
                    checks["sql_configured"] = !string.IsNullOrWhiteSpace(sqlConnString);
                    
                    var openAiEndpoint = _configuration["AzureOpenAI:Endpoint"];
                    checks["openai_configured"] = !string.IsNullOrWhiteSpace(openAiEndpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Configuration check failed");
                    checks["configuration_error"] = ex.Message;
                    isHealthy = false;
                }

                // 3. Database connectivity (lightweight check)
                try
                {
                    var dbHealthy = await CheckDatabaseHealthAsync();
                    checks["database"] = dbHealthy ? "connected" : "unavailable";
                    if (!dbHealthy) isHealthy = false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Database health check failed");
                    checks["database"] = "error";
                    checks["database_error"] = ex.Message;
                    isHealthy = false;
                }

                // 4. Performance metrics
                var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                checks["response_time_ms"] = Math.Round(responseTime, 2);

                if (isHealthy)
                {
                    _logger.LogDebug("Health check passed: {ResponseTime}ms", responseTime);
                    return new OkObjectResult(checks);
                }
                else
                {
                    _logger.LogWarning("Health check failed: {ResponseTime}ms", responseTime);
                    return new ObjectResult(checks) { StatusCode = 503 };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check exception");
                
                return new ObjectResult(new
                {
                    status = "error",
                    timestamp = DateTime.UtcNow,
                    error = ex.Message
                })
                { StatusCode = 503 };
            }
        }

        private async Task<bool> CheckDatabaseHealthAsync()
        {
            try
            {
                var connectionString = _configuration["ConnectionStrings:SqlDb"];
                if (string.IsNullOrWhiteSpace(connectionString))
                    return false;

                using var conn = new SqlConnection(connectionString);
                
                // Set short timeout for health check
                conn.ConnectionString = new SqlConnectionStringBuilder(connectionString)
                {
                    ConnectTimeout = 5 // 5 seconds
                }.ConnectionString;

                await conn.OpenAsync();
                
                // Simple query to verify connectivity
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                cmd.CommandTimeout = 5;
                await cmd.ExecuteScalarAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database connectivity check failed");
                return false;
            }
        }

        private static string GetUptime()
        {
            try
            {
                var uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
                return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
            }
            catch
            {
                return "unknown";
            }
        }

        private static long GetMemoryUsageMB()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return process.WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }
    }
}
