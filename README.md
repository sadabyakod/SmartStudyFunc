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
