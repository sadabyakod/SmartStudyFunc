using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using SmartStudyFunc.Services;
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
