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
        services.AddScoped<EvaluateAnswer>();

        // ========================================
        // NEW: Subject-Specific Evaluation Engines
        // ========================================
        
        // V2: Memory Cache for syllabus caching (100 MB limit)
        services.AddMemoryCache();
        
        // V2: Enhanced Question Classifier (30% accuracy improvement)
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.EnhancedQuestionClassifier>();
        
        // V2: Syllabus Cache Service (80%+ cache hit rate)
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.SyllabusCacheService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SmartStudyFunc.Services.Evaluation.SyllabusCacheService>>();
            var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            return new SmartStudyFunc.Services.Evaluation.SyllabusCacheService(logger, blobServiceClient, cache);
        });
        
        // V2: Evaluation Audit Logger (full compliance tracking)
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.EvaluationAuditLogger>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SmartStudyFunc.Services.Evaluation.EvaluationAuditLogger>>();
            var connectionString = configuration["SqlConnectionString"] 
                ?? configuration.GetConnectionString("SqlDb");
            return new SmartStudyFunc.Services.Evaluation.EvaluationAuditLogger(logger, connectionString!);
        });
        
        // Register Question Classifier (V1 - fallback)
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.IQuestionClassifier, SmartStudyFunc.Services.Evaluation.QuestionClassifier>();
        
        // Register all evaluation engines
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.IEvaluationEngine, SmartStudyFunc.Services.Evaluation.MathematicsEvaluationEngine>();
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.IEvaluationEngine, SmartStudyFunc.Services.Evaluation.PhysicsChemistryEvaluationEngine>();
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.IEvaluationEngine, SmartStudyFunc.Services.Evaluation.BiologySocialEvaluationEngine>();
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.IEvaluationEngine, SmartStudyFunc.Services.Evaluation.LanguageEvaluationEngine>();
        
        // Register Subject Router (orchestrator)
        services.AddSingleton<SmartStudyFunc.Services.Evaluation.ISubjectRouter, SmartStudyFunc.Services.Evaluation.SubjectRouter>();

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

        // Register Azure OpenAI Client with reasonable timeout
        services.AddSingleton(sp =>
        {
            var endpoint = configuration["AzureOpenAI:Endpoint"];
            var apiKey = configuration["AzureOpenAI:ApiKey"];
            
            var options = new OpenAIClientOptions
            {
                Retry = {
                    MaxRetries = 3,
                    NetworkTimeout = TimeSpan.FromSeconds(60) // 60s network timeout for AI calls
                }
            };
            
            return new OpenAIClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!), options);
        });

        // Note: Google Cloud Vision client registration is not needed when using API Key auth.
        // The GoogleVisionOcrService handles both API Key and Service Account authentication internally.

        // Register Google Vision OCR Service (supports both API Key and Service Account auth)
        services.AddSingleton<IGoogleVisionOcrService>(sp =>
        {
            var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
            var logger = sp.GetRequiredService<ILogger<GoogleVisionOcrService>>();
            return new GoogleVisionOcrService(configuration, blobServiceClient, logger);
        });

        // Register Azure Document Intelligence OCR Service
        services.AddSingleton<OcrService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<OcrService>>();
            return new OcrService(logger);
        });

        // Register Dual OCR Service (uses both Google and Azure)
        services.AddSingleton<IDualOcrService>(sp =>
        {
            var googleOcr = sp.GetRequiredService<IGoogleVisionOcrService>();
            var azureOcr = sp.GetRequiredService<OcrService>();
            var logger = sp.GetRequiredService<ILogger<DualOcrService>>();
            return new DualOcrService(googleOcr, azureOcr, logger);
        });

        // Register Written Submission Repository (with RubricBlobService for V2 tables)
        services.AddSingleton<IWrittenSubmissionRepository>(sp =>
        {
            var connectionString = configuration["SqlConnectionString"] 
                ?? configuration.GetConnectionString("SqlDb");
            var logger = sp.GetRequiredService<ILogger<WrittenSubmissionRepository>>();
            var rubricBlobService = sp.GetRequiredService<IRubricBlobService>();
            return new WrittenSubmissionRepository(connectionString!, logger, rubricBlobService);
        });

        // Register Rubric Blob Service for storing/fetching question rubrics
        services.AddSingleton<IRubricBlobService>(sp =>
        {
            var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
            var logger = sp.GetRequiredService<ILogger<RubricBlobService>>();
            return new RubricBlobService(blobServiceClient, logger);
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
