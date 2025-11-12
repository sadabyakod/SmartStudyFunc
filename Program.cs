using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartStudyFunc.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Register custom services for dependency injection
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<SmartStudyFunc.OpenAiService>();
    })
    .Build();

host.Run();
