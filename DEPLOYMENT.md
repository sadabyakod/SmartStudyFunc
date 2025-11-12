# Deployment Guide - SmartStudyFunc

## Git Repository Setup ✅

Your code has been committed to local Git repository.

### Push to GitHub/Azure DevOps

```powershell
# Add remote repository (replace with your repository URL)
git remote add origin https://github.com/yourusername/SmartStudyFunc.git

# Push to remote
git push -u origin master
```

## Publish to Azure Function App

### Prerequisites
1. Azure Function App created in Azure Portal
2. Azure CLI installed (optional but recommended)

### Method 1: Using Azure Functions Core Tools (Recommended)

```powershell
# Publish to your Azure Function App
func azure functionapp publish <YOUR_FUNCTION_APP_NAME>
```

### Method 2: Create Function App and Publish via Azure Portal

1. **Create Function App:**
   - Go to Azure Portal → Create a resource → Function App
   - Fill in details:
     - **App name**: Choose a unique name (e.g., `smartstudyfunc-app`)
     - **Runtime stack**: .NET
     - **Version**: 8 (Isolated)
     - **Region**: Choose your preferred region
     - **Storage account**: Use existing `studyaistorage345` or create new

2. **Deploy Code:**
   - After creating Function App, use:
   ```powershell
   func azure functionapp publish <YOUR_FUNCTION_APP_NAME>
   ```

### Method 3: Using VS Code Azure Functions Extension

1. Install "Azure Functions" extension in VS Code
2. Sign in to Azure
3. Right-click on the project → "Deploy to Function App"
4. Select or create Function App
5. Confirm deployment

### Method 4: CI/CD with GitHub Actions

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy Azure Function

on:
  push:
    branches: [ master ]

jobs:
  build-and-deploy:
    runs-on: windows-latest
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '8.0.x'
    
    - name: Build
      run: dotnet build --configuration Release
    
    - name: Publish
      run: dotnet publish --configuration Release --output ./output
    
    - name: Deploy to Azure Functions
      uses: Azure/functions-action@v1
      with:
        app-name: '<YOUR_FUNCTION_APP_NAME>'
        package: './output'
        publish-profile: ${{ secrets.AZURE_FUNCTIONAPP_PUBLISH_PROFILE }}
```

## Configuration in Azure

After deployment, configure these Application Settings in Azure Portal:

1. Go to Function App → Configuration → Application Settings
2. Add/Update:
   - `AzureWebJobsStorage`: Your storage connection string
   - `FUNCTIONS_WORKER_RUNTIME`: `dotnet-isolated`

**Connection String Format:**
```
DefaultEndpointsProtocol=https;AccountName=<YOUR_STORAGE_ACCOUNT_NAME>;AccountKey=<YOUR_ACCOUNT_KEY>;EndpointSuffix=core.windows.net
```

> **Note:** Replace `<YOUR_STORAGE_ACCOUNT_NAME>` and `<YOUR_ACCOUNT_KEY>` with your actual Azure Storage credentials. You can find these in Azure Portal → Storage Account → Access Keys.

## Verify Deployment

1. In Azure Portal, go to your Function App
2. Click "Functions" → You should see `ProcessBlobFile`
3. Upload a file to `textbooks` container
4. Check "Monitor" or "Logs" to see execution logs

## Quick Publish Command

```powershell
# If you have Azure Function App named 'smartstudyfunc-app'
func azure functionapp publish smartstudyfunc-app
```

## Troubleshooting

- **Authentication error**: Run `az login` or `Connect-AzAccount`
- **Build error**: Ensure .NET 8 SDK is installed
- **Connection error**: Verify storage connection string in Azure settings
