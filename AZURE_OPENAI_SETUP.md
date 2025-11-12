# Azure OpenAI Integration Guide

## Overview

Your Azure Function now supports **real Azure OpenAI embeddings** in addition to the fake embeddings used for testing.

## Files Created/Updated

### 1. `OpenAiService.cs` (NEW)
A complete Azure OpenAI service implementation with:
- **`GetEmbeddingBytesAsync(string input)`**: Gets embeddings from Azure OpenAI and returns as byte array
- **`GetChatCompletionAsync(string prompt, IEnumerable<string> systemMessages)`**: Gets chat completions
- Automatic retry logic (3 attempts) for transient errors (429, 5xx, timeouts)
- Detailed logging and error handling

### 2. `ProcessBlobFile.cs` (UPDATED)
- `EmbeddingService` now supports both real and fake embeddings
- Uses `USE_REAL_EMBEDDINGS` environment variable to switch between modes
- Automatic fallback to fake embeddings if OpenAI call fails

### 3. `local.settings.json` (UPDATED)
Added Azure OpenAI configuration placeholders

## Configuration

### Required Settings in `local.settings.json`

```json
{
  "Values": {
    "USE_REAL_EMBEDDINGS": "true",
    "AzureOpenAI__Endpoint": "https://your-resource-name.openai.azure.com/",
    "AzureOpenAI__Key": "your-azure-openai-api-key",
    "AzureOpenAI__DeploymentEmbedding": "text-embedding-ada-002",
    "AzureOpenAI__DeploymentChat": "gpt-4"
  }
}
```

### How to Get Azure OpenAI Credentials

1. **Create Azure OpenAI Resource** (if you don't have one):
   - Go to [Azure Portal](https://portal.azure.com)
   - Create a new resource → Search for "Azure OpenAI"
   - Create the resource in your subscription

2. **Get Endpoint and Key**:
   - Go to your Azure OpenAI resource
   - Click **Keys and Endpoint**
   - Copy:
     - **Endpoint**: `https://your-resource-name.openai.azure.com/`
     - **Key**: One of the two keys shown

3. **Deploy Models**:
   - In Azure OpenAI resource, click **Model deployments** → **Create**
   - Deploy these models:
     - **Embedding Model**: `text-embedding-ada-002` (deployment name: `text-embedding-ada-002`)
     - **Chat Model**: `gpt-4` or `gpt-35-turbo` (deployment name: `gpt-4` or `gpt-35-turbo`)

4. **Update Configuration**:
   ```json
   {
     "AzureOpenAI__Endpoint": "https://your-actual-resource.openai.azure.com/",
     "AzureOpenAI__Key": "your-actual-api-key-from-portal",
     "AzureOpenAI__DeploymentEmbedding": "text-embedding-ada-002",
     "AzureOpenAI__DeploymentChat": "gpt-4"
   }
   ```

## Usage Modes

### Mode 1: Fake Embeddings (Default - Free, No OpenAI needed)
```json
{
  "USE_REAL_EMBEDDINGS": "false"
}
```
- Uses deterministic random embeddings based on text hash
- Perfect for testing and development
- No costs, no API calls

### Mode 2: Real Azure OpenAI Embeddings
```json
{
  "USE_REAL_EMBEDDINGS": "true"
}
```
- Calls Azure OpenAI API for real embeddings
- Better quality for production use
- Requires valid Azure OpenAI configuration
- Incurs costs based on Azure OpenAI pricing

## Testing

### Test with Fake Embeddings (No Setup Required)
```powershell
# Ensure USE_REAL_EMBEDDINGS is false or not set
func start
# Upload a PDF to test - embeddings will be fake but function works
```

### Test with Real Embeddings
```powershell
# 1. Update local.settings.json with your Azure OpenAI credentials
# 2. Set USE_REAL_EMBEDDINGS to "true"
# 3. Start function
func start

# 4. Upload a PDF file to textbooks container
# 5. Check logs - you should see real OpenAI API calls
```

## Azure Function App Configuration

When deploying to Azure, add these Application Settings:

1. Go to Azure Portal → Your Function App → Configuration → Application Settings
2. Add:
   - `USE_REAL_EMBEDDINGS` = `true`
   - `AzureOpenAI__Endpoint` = `https://your-resource.openai.azure.com/`
   - `AzureOpenAI__Key` = `your-api-key`
   - `AzureOpenAI__DeploymentEmbedding` = `text-embedding-ada-002`
   - `AzureOpenAI__DeploymentChat` = `gpt-4`

## Error Handling

The service includes:
- **Automatic Retry**: 3 attempts with exponential backoff
- **Graceful Fallback**: If real embeddings fail, falls back to fake embeddings
- **Detailed Logging**: All errors logged with context
- **Transient Error Detection**: Handles 429 (rate limit), 5xx (server errors), timeouts

## Cost Considerations

### Embedding Costs (text-embedding-ada-002)
- Approximately $0.0001 per 1,000 tokens
- For a 1MB PDF (~250,000 tokens) split into chunks: ~$0.025

### Example Cost Calculation
- 100 PDFs/day × 250,000 tokens each = 25M tokens
- 25M tokens × $0.0001/1K tokens = **$2.50/day**

**Recommendation**: Start with fake embeddings for development, switch to real for production.

## Using Chat Completions (Future Enhancement)

The `OpenAiService` also provides `GetChatCompletionAsync` for future use:

```csharp
var openAiService = new OpenAiService(configuration, logger);

var systemMessages = new[] {
    "You are a helpful AI assistant that summarizes educational content."
};

var summary = await openAiService.GetChatCompletionAsync(
    "Summarize this text: " + chunkText,
    systemMessages
);
```

This could be used to:
- Generate better summaries for chunks
- Generate topic titles intelligently
- Answer questions about uploaded content
- Create study guides from PDFs

## Troubleshooting

### Error: "Missing configuration: AzureOpenAI:Endpoint"
- Check `local.settings.json` has `AzureOpenAI__Endpoint` in Values section
- Verify double underscore `__` (not single `:`)

### Error: "401 Unauthorized" or "Access denied"
- Verify your API key is correct
- Check if the key is still valid (not regenerated)
- Ensure your Azure OpenAI resource is active

### Error: "404 Not Found" or "DeploymentNotFound"
- Verify deployment names match exactly (case-sensitive)
- Go to Azure OpenAI → Model deployments → Check deployment names
- Common names: `text-embedding-ada-002`, `gpt-4`, `gpt-35-turbo`

### Error: "429 Too Many Requests"
- You're hitting rate limits
- The retry logic will handle this automatically
- Consider increasing quota in Azure OpenAI resource

### Function Falls Back to Fake Embeddings
- This is expected behavior when `USE_REAL_EMBEDDINGS` is false
- Check logs for the message: "Using real Azure OpenAI embeddings" vs "Using fake embeddings"
- If you expect real embeddings but getting fake, check configuration

## Package Information

**Required NuGet Package**: 
```xml
<PackageReference Include="Azure.AI.OpenAI" Version="1.0.0-beta.17" />
```

Already installed in your project via:
```powershell
dotnet add package Azure.AI.OpenAI --version 1.0.0-beta.17
```

## Next Steps

1. **For Development**: Keep `USE_REAL_EMBEDDINGS=false` and test with fake embeddings
2. **For Production**: 
   - Create Azure OpenAI resource
   - Deploy embedding model
   - Update configuration
   - Set `USE_REAL_EMBEDDINGS=true`
   - Test with a small PDF first
   - Monitor costs in Azure Cost Management

## Summary

✅ **Created**: `OpenAiService.cs` with full Azure OpenAI integration  
✅ **Updated**: `EmbeddingService` to support real/fake modes  
✅ **Updated**: `local.settings.json` with configuration placeholders  
✅ **Installed**: `Azure.AI.OpenAI` package (v1.0.0-beta.17)  
✅ **Built**: Successfully compiled with 0 errors  

Your function is now ready to use either fake or real embeddings based on configuration!
