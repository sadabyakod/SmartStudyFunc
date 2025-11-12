# Azure OpenAI Real Embeddings - Configuration Summary

## ✅ Configuration Complete

Your Azure Function is now configured to use **real Azure OpenAI embeddings** with the `text-embedding-3-large` model.

## Current Configuration

### local.settings.json
```json
{
  "USE_REAL_EMBEDDINGS": "true",
  "AzureOpenAI__Endpoint": "https://your-resource.openai.azure.com/",
  "AzureOpenAI__ApiKey": "your-api-key-here",
  "AzureOpenAI__EmbeddingDeployment": "text-embedding-3-large",
  "AzureOpenAI__DeploymentChat": "gpt-4o-mini"
}
```

## Next Steps to Deploy Models

### 1. Deploy text-embedding-3-large

1. Go to [Azure Portal](https://portal.azure.com) → Your OpenAI resource `smartstudyai`
2. Click **"Go to Azure OpenAI Studio"** (or **"Model deployments"**)
3. Click **"+ Create new deployment"**
4. Select: **`text-embedding-3-large`**
5. Deployment name: **`text-embedding-3-large`** (exact match)
6. Tokens per minute: **60K-120K**
7. Click **"Create"**

### 2. Deploy gpt-4o-mini

1. Click **"+ Create new deployment"** again
2. Select: **`gpt-4o-mini`**
3. Deployment name: **`gpt-4o-mini`** (exact match)
4. Tokens per minute: **30K-80K**
5. Click **"Create"**

## Testing

Once models are deployed:

```powershell
# Start the function
func start

# Upload a PDF to the textbooks container
# Watch the logs - you should see real OpenAI API calls
```

### Expected Log Output
```
[Info] OpenAI Service initialized with Endpoint=https://smartstudyai.openai.azure.com/, EmbeddingModel=text-embedding-3-large, ChatModel=gpt-4o-mini
[Info] EmbeddingService: Using real Azure OpenAI embeddings
[Debug] Requesting embedding (attempt 1/3)
[Debug] Received embedding with 3072 dimensions
```

## Model Specifications

### text-embedding-3-large
- **Dimensions**: 3072 (vs 1536 for ada-002)
- **Better quality**: More accurate semantic understanding
- **Cost**: ~$0.00013 per 1K tokens
- **Use case**: Production-grade embeddings for RAG/search

### gpt-4o-mini
- **Fast and cost-effective**: 15x cheaper than GPT-4
- **Good quality**: Better than GPT-3.5-turbo
- **Cost**: ~$0.15 per 1M input tokens
- **Use case**: Chat completions, summarization

## Troubleshooting

### "Deployment not found"
- Verify deployment names match exactly: `text-embedding-3-large` and `gpt-4o-mini`
- Check deployments are in **"Succeeded"** status in Azure AI Studio

### "401 Unauthorized"
- Verify API key is correct
- Check if key was regenerated in Azure Portal

### "429 Rate Limited"
- Increase tokens per minute in deployment settings
- The service has automatic retry logic (3 attempts)

### Falls back to fake embeddings
- Check logs for errors
- Verify `USE_REAL_EMBEDDINGS` is set to `"true"`
- Ensure all configuration values are correct

## Cost Estimate

For a 1MB PDF (~250K tokens) split into 43 chunks:

**Embeddings (text-embedding-3-large)**:
- 43 chunks × ~400 tokens/chunk = 17,200 tokens
- 17,200 tokens × $0.00013/1K = **~$0.0022 per PDF**

**For 100 PDFs/day**: ~$0.22/day or **~$6.60/month**

Much more affordable than GPT-4 for embeddings!

## Switch Back to Fake Embeddings (If Needed)

If you want to test without API calls:

```json
{
  "USE_REAL_EMBEDDINGS": "false"
}
```

The function will automatically use fake embeddings without any code changes.

---

## Summary

✅ **Package**: Azure.AI.OpenAI v1.0.0-beta.17 installed  
✅ **Configuration**: Updated to use text-embedding-3-large  
✅ **Real embeddings**: Enabled (USE_REAL_EMBEDDINGS=true)  
✅ **Build**: Successful (0 errors)  
⏳ **Next**: Deploy models in Azure AI Studio  

Once you deploy the models, your function will automatically start using real Azure OpenAI embeddings!
