<#
.SYNOPSIS
    Configures Azure OpenAI settings in local.settings.json

.DESCRIPTION
    This script prompts for or accepts Azure OpenAI configuration parameters
    and updates the local.settings.json file while preserving other settings.

.PARAMETER Endpoint
    Azure OpenAI resource endpoint (e.g., https://my-openai.openai.azure.com/)

.PARAMETER ApiKey
    Azure OpenAI API key from the Azure Portal

.PARAMETER EmbeddingDeployment
    Name of the embedding model deployment (e.g., text-embedding-3-large)

.PARAMETER ChatDeployment
    Name of the chat model deployment (e.g., gpt-4o-mini)

.EXAMPLE
    .\set-azureopenai-config.ps1
    Runs interactively, prompting for all values

.EXAMPLE
    .\set-azureopenai-config.ps1 -Endpoint "https://my-openai.openai.azure.com/" -ApiKey "abc123..." -EmbeddingDeployment "text-embedding-3-large" -ChatDeployment "gpt-4o-mini"
    Sets all values via parameters
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$Endpoint,

    [Parameter(Mandatory=$false)]
    [string]$ApiKey,

    [Parameter(Mandatory=$false)]
    [string]$EmbeddingDeployment = "text-embedding-3-large",

    [Parameter(Mandatory=$false)]
    [string]$ChatDeployment = "gpt-4o-mini"
)

$ErrorActionPreference = "Stop"
$settingsPath = Join-Path $PSScriptRoot "..\local.settings.json"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Azure OpenAI Configuration Helper" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Prompt for values if not provided
if ([string]::IsNullOrWhiteSpace($Endpoint)) {
    Write-Host "Enter your Azure OpenAI Endpoint:" -ForegroundColor Yellow
    Write-Host "  (e.g., https://my-resource.openai.azure.com/)" -ForegroundColor Gray
    $Endpoint = Read-Host "Endpoint"
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host ""
    Write-Host "Enter your Azure OpenAI API Key:" -ForegroundColor Yellow
    Write-Host "  (Find this in Azure Portal → Your OpenAI Resource → Keys and Endpoint)" -ForegroundColor Gray
    $ApiKey = Read-Host "API Key" -AsSecureString
    $ApiKey = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($ApiKey))
}

if ([string]::IsNullOrWhiteSpace($EmbeddingDeployment)) {
    Write-Host ""
    Write-Host "Enter your Embedding Model Deployment Name:" -ForegroundColor Yellow
    Write-Host "  (Default: text-embedding-3-large)" -ForegroundColor Gray
    $embInput = Read-Host "Embedding Deployment"
    if (![string]::IsNullOrWhiteSpace($embInput)) {
        $EmbeddingDeployment = $embInput
    }
}

if ([string]::IsNullOrWhiteSpace($ChatDeployment)) {
    Write-Host ""
    Write-Host "Enter your Chat Model Deployment Name:" -ForegroundColor Yellow
    Write-Host "  (Default: gpt-4o-mini)" -ForegroundColor Gray
    $chatInput = Read-Host "Chat Deployment"
    if (![string]::IsNullOrWhiteSpace($chatInput)) {
        $ChatDeployment = $chatInput
    }
}

# Validate endpoint format
if (-not $Endpoint.StartsWith("https://")) {
    Write-Host ""
    Write-Host "ERROR: Endpoint must start with 'https://'" -ForegroundColor Red
    Write-Host "  You provided: $Endpoint" -ForegroundColor Red
    exit 1
}

if (-not $Endpoint.EndsWith("/")) {
    $Endpoint = $Endpoint + "/"
}

# Load existing settings or create new
$settings = $null
if (Test-Path $settingsPath) {
    Write-Host ""
    Write-Host "Loading existing local.settings.json..." -ForegroundColor Green
    $settingsContent = Get-Content $settingsPath -Raw
    $settings = $settingsContent | ConvertFrom-Json
} else {
    Write-Host ""
    Write-Host "Creating new local.settings.json..." -ForegroundColor Yellow
    $settings = @{
        IsEncrypted = $false
        Values = @{}
        ConnectionStrings = @{}
    }
}

# Ensure Values section exists
if ($null -eq $settings.Values) {
    $settings | Add-Member -MemberType NoteProperty -Name "Values" -Value @{} -Force
}

# Update or add Azure OpenAI settings
$settings.Values."USE_REAL_EMBEDDINGS" = "true"
$settings.Values."AzureOpenAI:Endpoint" = $Endpoint
$settings.Values."AzureOpenAI:ApiKey" = $ApiKey
$settings.Values."AzureOpenAI:EmbeddingDeployment" = $EmbeddingDeployment
$settings.Values."AzureOpenAI:ChatDeployment" = $ChatDeployment

# Ensure other required settings exist
if ([string]::IsNullOrWhiteSpace($settings.Values."FUNCTIONS_WORKER_RUNTIME")) {
    $settings.Values."FUNCTIONS_WORKER_RUNTIME" = "dotnet-isolated"
}

if ([string]::IsNullOrWhiteSpace($settings.Values."AzureWebJobsStorage")) {
    $settings.Values."AzureWebJobsStorage" = "UseDevelopmentStorage=true"
}

# Save updated settings
$settings | ConvertTo-Json -Depth 10 | Set-Content $settingsPath -Encoding UTF8

Write-Host ""
Write-Host "✓ Configuration updated successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Settings saved to: $settingsPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration Summary:" -ForegroundColor Yellow
Write-Host "  Endpoint:            $Endpoint" -ForegroundColor White
Write-Host "  API Key:             $($ApiKey.Substring(0, [Math]::Min(10, $ApiKey.Length)))..." -ForegroundColor White
Write-Host "  Embedding Model:     $EmbeddingDeployment" -ForegroundColor White
Write-Host "  Chat Model:          $ChatDeployment" -ForegroundColor White
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Verify your deployment names match exactly in Azure AI Studio" -ForegroundColor Gray
Write-Host "  2. Run validation: dotnet run --project Tools/OpenAiConfigValidator" -ForegroundColor Gray
Write-Host "  3. Start your function: func start" -ForegroundColor Gray
Write-Host ""
