# SmartStudyFunc - Azure Functions Project

This project contains an Azure Function with a BlobTrigger that processes files uploaded to Azure Blob Storage.

## Function: ProcessBlobFile

**Trigger Type:** BlobTrigger  
**Target Framework:** .NET 8.0 (Isolated Worker Model)  
**Container:** `uploads`

### Features
- Automatically triggers when a new file is uploaded to the `uploads` container
- Logs blob file name, size, and full path
- Includes error handling with try-catch
- Contains a dedicated section for custom file processing logic
- Compatible with Azure Storage Emulator (Azurite) for local development

## Local Development Setup

### Prerequisites
- .NET 8 SDK
- Azure Functions Core Tools v4
- Azurite (Azure Storage Emulator) or Azure Storage Account

### Running Locally

1. **Install dependencies:**
   ```powershell
   dotnet restore
   ```

2. **Start Azurite (for local storage emulation):**
   ```powershell
   azurite --silent --location c:\azurite --debug c:\azurite\debug.log
   ```

3. **Run the function:**
   ```powershell
   func start
   ```

4. **Test the function:**
   Upload a file to the `uploads` container using Azure Storage Explorer or programmatically.

### Configuration

Update `local.settings.json` with your Azure Storage connection string:

```json
{
  "Values": {
    "AzureWebJobsStorage": "DefaultEndpointsProtocol=https;AccountName=<your-account>;AccountKey=<your-key>;EndpointSuffix=core.windows.net"
  }
}
```

For local development with Azurite, use:
```json
"AzureWebJobsStorage": "UseDevelopmentStorage=true"
```

## Deployment

Deploy to Azure using:
```powershell
func azure functionapp publish <your-function-app-name>
```

## Custom Processing

Add your custom file processing logic in the designated section within `ProcessBlobFile.cs`. Examples include:
- Parsing CSV, JSON, or XML files
- Image processing or text extraction
- Moving files to different containers
- Saving metadata to databases
- Triggering downstream processes

---

## Step-5: Azure OpenAI Configuration & Validation

This section guides you through configuring Azure OpenAI for real embeddings and chat completions.

### Prerequisites

1. **Azure OpenAI Resource**: Create an Azure OpenAI resource in the Azure Portal
2. **Model Deployments**: Deploy the following models in Azure AI Studio:
   - `text-embedding-3-large` (or `text-embedding-ada-002`)
   - `gpt-4o-mini` (or `gpt-4`, `gpt-35-turbo`)

### Getting Your Configuration Values

#### 1. Get Endpoint and API Key

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to your Azure OpenAI resource
3. Click **Keys and Endpoint** in the left menu
4. Copy:
   - **Endpoint**: `https://your-resource.openai.azure.com/`
   - **Key**: One of the two keys shown (Key 1 or Key 2)

#### 2. Verify Deployment Names

1. Go to your Azure OpenAI resource
2. Click **Model deployments** or **Go to Azure OpenAI Studio**
3. Note the **exact deployment names** (case-sensitive):
   - Example: `text-embedding-3-large`
   - Example: `gpt-4o-mini`

**Important**: Deployment names must match EXACTLY as they appear in Azure AI Studio.

### Configuration Methods

#### Option 1: PowerShell Helper (Recommended)

Run the interactive configuration script:

```powershell
# Navigate to project root
cd c:\SmartStudyFunc

# Run the configuration helper
.\tools\set-azureopenai-config.ps1
```

The script will prompt you for:
- Azure OpenAI Endpoint
- API Key
- Embedding Deployment Name (default: `text-embedding-3-large`)
- Chat Deployment Name (default: `gpt-4o-mini`)

**Or** provide values as parameters:

```powershell
.\tools\set-azureopenai-config.ps1 `
  -Endpoint "https://my-openai.openai.azure.com/" `
  -ApiKey "your-api-key-here" `
  -EmbeddingDeployment "text-embedding-3-large" `
  -ChatDeployment "gpt-4o-mini"
```

#### Option 2: Manual Configuration

Edit `local.settings.json` directly:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "USE_REAL_EMBEDDINGS": "true",
    "AzureOpenAI:Endpoint": "https://YOUR-RESOURCE.openai.azure.com/",
    "AzureOpenAI:ApiKey": "PASTE-YOUR-KEY-HERE",
    "AzureOpenAI:EmbeddingDeployment": "text-embedding-3-large",
    "AzureOpenAI:ChatDeployment": "gpt-4o-mini"
  },
  "ConnectionStrings": {
    "SqlDb": "Your-SQL-Connection-String"
  }
}
```

### Validate Your Configuration

After configuring, validate your setup using the PowerShell test script:

```powershell
# Run the validation test
.\tools\test-azureopenai-config.ps1
```

The script will:
1. Check that `local.settings.json` exists and contains required settings
2. Build your project
3. Make a test call to Azure OpenAI
4. Display detailed results and next steps

#### Understanding Validation Results

**✓ SUCCESS**
- Your configuration is correct
- Deployment is accessible
- Shows embedding dimensions (e.g., 3072 for text-embedding-3-large)
- You can proceed to run `func start`

**⚠ NOT CONFIGURED**
- `USE_REAL_EMBEDDINGS` is not set to `true`, or
- Missing Endpoint/ApiKey configuration
- **Action**: Run `.\tools\set-azureopenai-config.ps1`

**✗ DEPLOYMENT NOT FOUND (HTTP 404)**
- The deployment name doesn't exist or doesn't match exactly
- **Common causes**:
  - Typo in deployment name (check case-sensitivity)
  - Deployment not created in Azure AI Studio
  - Using wrong resource endpoint
- **Action**: 
  1. Go to Azure Portal → Your OpenAI Resource → Model deployments
  2. Verify deployment name matches EXACTLY
  3. Create deployment if it doesn't exist
  4. Run `.\tools\set-azureopenai-config.ps1` to update settings

**✗ UNAUTHORIZED (HTTP 401/403)**
- API key is incorrect or has been regenerated
- **Action**:
  1. Go to Azure Portal → Your OpenAI Resource → Keys and Endpoint
  2. Copy the correct key
  3. Run `.\tools\set-azureopenai-config.ps1` to update settings

**⚠ RATE LIMIT EXCEEDED (HTTP 429)**
- You've hit your Azure OpenAI quota limits
- **Action**:
  1. Wait a few moments and retry
  2. Check Azure Portal for quota settings
  3. Increase TPM (tokens per minute) if needed

**✗ SERVER ERROR (HTTP 5xx)**
- Azure OpenAI service is experiencing issues
- **Action**:
  1. Wait a few moments and retry
  2. Check Azure status page for service health

### Testing with Your Function

Once validation succeeds:

```powershell
# Start the Azure Function
func start

# Upload a PDF to test embedding generation
# Check logs to see real Azure OpenAI embeddings in action
```

### Troubleshooting

**Q: Validator says "deployment not found" but I created it**
- Verify the deployment name is spelled exactly as it appears in Azure AI Studio
- Deployment names are case-sensitive
- Common names: `text-embedding-3-large`, `text-embedding-ada-002`, `gpt-4o-mini`

**Q: Getting authentication errors**
- Ensure you copied the entire API key (no spaces/truncation)
- Check if the key was regenerated in Azure Portal
- Verify you're using the correct Azure OpenAI resource endpoint

**Q: Validation works but function fails**
- Check that both embedding AND chat deployments are configured
- Verify SQL connection string is also set correctly
- Review function logs for specific errors

**Q: Want to use fake embeddings for testing**
- Set `USE_REAL_EMBEDDINGS` to `false` in `local.settings.json`
- No Azure OpenAI calls will be made
- Useful for offline development

### Configuration Template

A template file is provided at `local.settings.template.json` for reference.

### Security Notes

- Never commit `local.settings.json` to source control (already in `.gitignore`)
- Regenerate API keys if accidentally exposed
- Use managed identities in production instead of API keys where possible

---

## Additional Resources

- [Azure OpenAI Documentation](https://learn.microsoft.com/azure/ai-services/openai/)
- [Model Deployments Guide](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource)
- [Embeddings Models Overview](https://platform.openai.com/docs/guides/embeddings)

