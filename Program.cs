using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Services;
using SmartStudyFunc.Functions;
using Azure.Storage.Blobs;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Register custom services for dependency injection
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<SmartStudyFunc.OpenAiService>();
        
        // Register evaluation services
        services.AddSingleton<OcrService>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<OcrService>>();
            return new OcrService(logger);
        });
        
        services.AddSingleton<AiScoringService>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<AiScoringService>>();
            return new AiScoringService(logger);
        });
        
        // Register function classes for batch evaluation dependency
        services.AddScoped<EvaluateAnswer>();
        
        // Register BlobServiceClient for upload functionality
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = configuration["AzureWebJobsStorage"];
            return new BlobServiceClient(connectionString);
        });
    })
    .Build();

host.Run();
