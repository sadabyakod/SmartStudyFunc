using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartStudyFunc.Services;
using SmartStudyFunc.Functions;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.AI.OpenAI;
using Azure;
using Google.Cloud.Vision.V1;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        
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
        services.AddTransient<EvaluateAnswer>();
        
        // ========================================
        // Azure Storage Clients
        // ========================================
        
        // Register BlobServiceClient
        services.AddSingleton(sp =>
        {
            var connectionString = configuration["AzureWebJobsStorage"];
            return new BlobServiceClient(connectionString);
        });
        
        // Register QueueServiceClient for re-enqueueing with retry
        services.AddSingleton(sp =>
        {
            var connectionString = configuration["AzureWebJobsStorage"];
            return new QueueServiceClient(connectionString);
        });
        
        // ========================================
        // Written Answer Processing Services
        // ========================================
        
        // Register Azure OpenAI Client
        services.AddSingleton(sp =>
        {
            var endpoint = configuration["AzureOpenAI:Endpoint"];
            var apiKey = configuration["AzureOpenAI:ApiKey"];
            return new OpenAIClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!));
        });
        
        // Register Google Cloud Vision Client
        services.AddSingleton(sp =>
        {
            // Uses GOOGLE_APPLICATION_CREDENTIALS environment variable
            return ImageAnnotatorClient.Create();
        });
        
        // Register Google Vision OCR Service
        services.AddSingleton<IGoogleVisionOcrService, GoogleVisionOcrService>();
        
        // Register Written Submission Repository
        services.AddSingleton<IWrittenSubmissionRepository>(sp =>
        {
            var connectionString = configuration["SqlConnectionString"] 
                ?? configuration.GetConnectionString("SqlDb");
            var logger = sp.GetRequiredService<ILogger<WrittenSubmissionRepository>>();
            return new WrittenSubmissionRepository(connectionString!, logger);
        });
        
        // Register Syllabus RAG Service for step-wise board blueprint evaluation
        services.AddSingleton<ISyllabusRagService>(sp =>
        {
            var embeddingService = sp.GetRequiredService<EmbeddingService>();
            var logger = sp.GetRequiredService<ILogger<SyllabusRagService>>();
            return new SyllabusRagService(configuration, embeddingService, logger);
        });
        
        // Register Written Answer Evaluation Service (with Syllabus RAG for step-wise evaluation)
        services.AddSingleton<IWrittenAnswerEvaluationService>(sp =>
        {
            var openAiClient = sp.GetRequiredService<OpenAIClient>();
            var syllabusRagService = sp.GetRequiredService<ISyllabusRagService>();
            var deploymentName = configuration["AzureOpenAI:DeploymentName"] 
                ?? configuration["AzureOpenAI:ChatDeployment"] 
                ?? "gpt-4";
            var logger = sp.GetRequiredService<ILogger<WrittenAnswerEvaluationService>>();
            return new WrittenAnswerEvaluationService(openAiClient, deploymentName, syllabusRagService, logger);
        });
    })
    .Build();

host.Run();
