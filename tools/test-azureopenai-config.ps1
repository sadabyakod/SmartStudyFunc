<#
.SYNOPSIS
    Tests Azure OpenAI configuration by running the embedding service validation.

.DESCRIPTION
    This script builds the SmartStudyFunc project and runs a quick validation
    of the Azure OpenAI configuration by invoking the ValidateDeploymentsAsync()
    method on the EmbeddingService. It provides clear feedback about whether
    the configuration is correct and actionable next steps if there are issues.

.EXAMPLE
    .\tools\test-azureopenai-config.ps1

.NOTES
    Prerequisites:
    - Run tools\set-azureopenai-config.ps1 first to configure settings
    - Ensure local.settings.json exists with valid Azure OpenAI configuration
#>

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# Color output functions
function Write-Success { param($Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Error { param($Message) Write-Host "✗ $Message" -ForegroundColor Red }
function Write-Warning { param($Message) Write-Host "⚠ $Message" -ForegroundColor Yellow }
function Write-Info { param($Message) Write-Host "ℹ $Message" -ForegroundColor Cyan }

Write-Host ""
Write-Host "Azure OpenAI Configuration Validator" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check if local.settings.json exists
$settingsPath = Join-Path $PSScriptRoot "..\local.settings.json"
if (-not (Test-Path $settingsPath)) {
    Write-Error "local.settings.json not found!"
    Write-Host ""
    Write-Warning "You need to configure your Azure OpenAI settings first."
    Write-Info "Run: .\tools\set-azureopenai-config.ps1"
    Write-Host ""
    exit 1
}

Write-Info "Found local.settings.json"

# Step 2: Read and validate configuration
try {
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    
    $endpoint = $settings.Values.AzureOpenAI_Endpoint
    $apiKey = $settings.Values.AzureOpenAI_ApiKey
    $embeddingDeployment = $settings.Values.AzureOpenAI_EmbeddingDeployment
    $useRealEmbeddings = $settings.Values.USE_REAL_EMBEDDINGS
    
    if ([string]::IsNullOrWhiteSpace($endpoint)) {
        Write-Error "AzureOpenAI_Endpoint is not configured"
        Write-Info "Run: .\tools\set-azureopenai-config.ps1"
        exit 1
    }
    
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        Write-Error "AzureOpenAI_ApiKey is not configured"
        Write-Info "Run: .\tools\set-azureopenai-config.ps1"
        exit 1
    }
    
    if ([string]::IsNullOrWhiteSpace($embeddingDeployment)) {
        Write-Error "AzureOpenAI_EmbeddingDeployment is not configured"
        Write-Info "Run: .\tools\set-azureopenai-config.ps1"
        exit 1
    }
    
    if ($useRealEmbeddings -ne "true") {
        Write-Warning "USE_REAL_EMBEDDINGS is not set to 'true'"
        Write-Info "Set it to 'true' in local.settings.json to enable real embeddings"
        exit 0
    }
    
    Write-Success "Configuration file contains required settings"
    Write-Host ""
    Write-Host "  Endpoint: $endpoint" -ForegroundColor Gray
    Write-Host "  Deployment: $embeddingDeployment" -ForegroundColor Gray
    Write-Host "  API Key: $('*' * 40)" -ForegroundColor Gray
    Write-Host ""
}
catch {
    Write-Error "Failed to read local.settings.json: $_"
    exit 1
}

# Step 3: Build the project (optional)
if (-not $SkipBuild) {
    Write-Info "Building project..."
    try {
        $buildOutput = dotnet build --nologo --verbosity quiet 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed"
            Write-Host $buildOutput
            exit 1
        }
        Write-Success "Project built successfully"
    }
    catch {
        Write-Error "Build error: $_"
        exit 1
    }
}

# Step 4: Make a test call to Azure OpenAI
Write-Info "Testing connection to Azure OpenAI..."
Write-Host ""

try {
    # Use Azure.AI.OpenAI directly for validation
    Add-Type -Path "C:\Users\Acer\.nuget\packages\azure.ai.openai\1.0.0-beta.17\lib\netstandard2.0\Azure.AI.OpenAI.dll"
    Add-Type -Path "C:\Users\Acer\.nuget\packages\azure.core\1.47.1\lib\net8.0\Azure.Core.dll"
    Add-Type -Path "C:\Users\Acer\.nuget\packages\system.clientmodel\1.5.1\lib\net8.0\System.ClientModel.dll"
    
    $credential = New-Object Azure.AzureKeyCredential($apiKey)
    $client = New-Object Azure.AI.OpenAI.AzureOpenAIClient([uri]$endpoint, $credential)
    
    # Make a test embedding call
    $testText = @("validate")
    $options = New-Object Azure.AI.OpenAI.EmbeddingsOptions($embeddingDeployment, $testText)
    
    $response = $client.GetEmbeddingsAsync($options).GetAwaiter().GetResult()
    $embedding = $response.Value.Data[0].Embedding
    
    Write-Success "SUCCESS! Azure OpenAI connection is working"
    Write-Host ""
    Write-Host "  Deployment: $embeddingDeployment" -ForegroundColor Green
    Write-Host "  Embedding dimensions: $($embedding.Length)" -ForegroundColor Green
    Write-Host ""
    Write-Success "Your configuration is ready to use!"
    Write-Host ""
    Write-Info "Next steps:"
    Write-Host "  1. Run: func start" -ForegroundColor Gray
    Write-Host "  2. Upload PDFs to test the system" -ForegroundColor Gray
    Write-Host ""
    exit 0
}
catch {
    $errorMessage = $_.Exception.Message
    
    Write-Error "Validation failed"
    Write-Host ""
    
    # Parse specific error types
    if ($errorMessage -like "*401*" -or $errorMessage -like "*Unauthorized*" -or $errorMessage -like "*403*") {
        Write-Host "❌ UNAUTHORIZED - API Key Issue" -ForegroundColor Red
        Write-Host ""
        Write-Host "Your API key appears to be invalid or expired." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "To fix this:" -ForegroundColor Cyan
        Write-Host "  1. Go to Azure Portal → Your OpenAI Resource" -ForegroundColor White
        Write-Host "  2. Navigate to 'Keys and Endpoint'" -ForegroundColor White
        Write-Host "  3. Copy KEY 1 or KEY 2" -ForegroundColor White
        Write-Host "  4. Run: .\tools\set-azureopenai-config.ps1" -ForegroundColor White
        Write-Host "  5. Paste the new API key when prompted" -ForegroundColor White
    }
    elseif ($errorMessage -like "*404*" -or $errorMessage -like "*NotFound*" -or $errorMessage -like "*DeploymentNotFound*") {
        Write-Host "❌ DEPLOYMENT NOT FOUND" -ForegroundColor Red
        Write-Host ""
        Write-Host "The deployment '$embeddingDeployment' was not found in your Azure OpenAI resource." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "To fix this:" -ForegroundColor Cyan
        Write-Host "  1. Go to Azure AI Studio (https://oai.azure.com/)" -ForegroundColor White
        Write-Host "  2. Select your resource" -ForegroundColor White
        Write-Host "  3. Go to 'Deployments' tab" -ForegroundColor White
        Write-Host "  4. Verify the exact deployment name" -ForegroundColor White
        Write-Host "  5. Run: .\tools\set-azureopenai-config.ps1" -ForegroundColor White
        Write-Host "  6. Enter the correct deployment name" -ForegroundColor White
        Write-Host ""
        Write-Warning "Deployment names are case-sensitive!"
    }
    elseif ($errorMessage -like "*429*" -or $errorMessage -like "*TooManyRequests*") {
        Write-Host "❌ RATE LIMIT EXCEEDED" -ForegroundColor Red
        Write-Host ""
        Write-Host "You've hit the rate limit for your Azure OpenAI resource." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "To fix this:" -ForegroundColor Cyan
        Write-Host "  1. Wait a few minutes and try again" -ForegroundColor White
        Write-Host "  2. Check your quota in Azure Portal" -ForegroundColor White
        Write-Host "  3. Consider increasing your TPM (tokens per minute) limit" -ForegroundColor White
    }
    elseif ($errorMessage -like "*5*") {
        Write-Host "❌ SERVER ERROR" -ForegroundColor Red
        Write-Host ""
        Write-Host "Azure OpenAI service is experiencing issues." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "To fix this:" -ForegroundColor Cyan
        Write-Host "  1. Wait a few minutes and try again" -ForegroundColor White
        Write-Host "  2. Check Azure status page" -ForegroundColor White
    }
    else {
        Write-Host "❌ UNKNOWN ERROR" -ForegroundColor Red
        Write-Host ""
        Write-Host "Error details:" -ForegroundColor Yellow
        Write-Host $errorMessage -ForegroundColor Gray
        Write-Host ""
        Write-Host "To fix this:" -ForegroundColor Cyan
        Write-Host "  1. Verify your endpoint URL starts with https://" -ForegroundColor White
        Write-Host "  2. Ensure you have network connectivity" -ForegroundColor White
        Write-Host "  3. Check firewall settings" -ForegroundColor White
    }
    
    Write-Host ""
    exit 1
}
