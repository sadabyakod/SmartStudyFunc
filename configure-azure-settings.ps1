# Azure Function App Settings Configuration
# SmartStudy Functions - App Settings Update
# ===========================================

param(
    [switch]$ShowCurrent,
    [switch]$UpdateAll
)

# Azure Configuration
$SubscriptionId = "64cf6114-e23f-4507-ba2f-7bf5b133a9fe"
$ResourceGroup = "rg-smartstudy-dev"
$FunctionAppName = "smartstudy-func"

function Write-Success { param($msg) Write-Host "✅ $msg" -ForegroundColor Green }
function Write-Info { param($msg) Write-Host "ℹ️  $msg" -ForegroundColor Cyan }
function Write-Warning { param($msg) Write-Host "⚠️  $msg" -ForegroundColor Yellow }

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║      SmartStudy Functions - App Settings Manager           ║" -ForegroundColor Magenta
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""

# Check Azure login
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Warning "Not logged in. Running 'az login'..."
    az login
}
az account set --subscription $SubscriptionId

if ($ShowCurrent) {
    Write-Info "Current App Settings for $FunctionAppName :"
    az functionapp config appsettings list `
        --resource-group $ResourceGroup `
        --name $FunctionAppName `
        --output table
    exit 0
}

# Required Application Settings
$appSettings = @{
    # Runtime Settings
    "FUNCTIONS_WORKER_RUNTIME" = "dotnet-isolated"
    "FUNCTIONS_EXTENSION_VERSION" = "~4"
    "WEBSITE_RUN_FROM_PACKAGE" = "1"
    
    # Feature Flags
    "USE_REAL_EMBEDDINGS" = "true"
    
    # Written Submission Settings
    "WrittenSubmission__RetentionDays" = "30"
}

# Settings that need to be configured manually (contain secrets)
$secretSettings = @(
    "AzureOpenAI__Endpoint",
    "AzureOpenAI__ApiKey",
    "AzureOpenAI__DeploymentName",
    "AzureOpenAI__EmbeddingDeployment",
    "AzureOpenAI__ChatDeployment",
    "SqlConnectionString",
    "GOOGLE_APPLICATION_CREDENTIALS"
)

if ($UpdateAll) {
    Write-Info "Updating application settings..."
    
    # Build settings string
    $settingsArray = @()
    foreach ($key in $appSettings.Keys) {
        $settingsArray += "$key=$($appSettings[$key])"
    }
    $settingsString = $settingsArray -join " "
    
    # Update settings
    az functionapp config appsettings set `
        --resource-group $ResourceGroup `
        --name $FunctionAppName `
        --settings $settingsArray
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Application settings updated successfully"
    } else {
        Write-Error "Failed to update settings"
    }
} else {
    Write-Info "Application Settings to Configure:"
    Write-Host ""
    
    Write-Host "Non-Secret Settings (will be set by -UpdateAll):" -ForegroundColor Cyan
    foreach ($key in $appSettings.Keys) {
        Write-Host "  $key = $($appSettings[$key])"
    }
    
    Write-Host ""
    Write-Host "Secret Settings (configure manually in Azure Portal or use Key Vault):" -ForegroundColor Yellow
    foreach ($setting in $secretSettings) {
        Write-Host "  $setting"
    }
    
    Write-Host ""
    Write-Host "Usage:" -ForegroundColor Cyan
    Write-Host "  .\configure-azure-settings.ps1 -ShowCurrent    # View current settings"
    Write-Host "  .\configure-azure-settings.ps1 -UpdateAll      # Apply non-secret settings"
    Write-Host ""
    Write-Host "To set secret values manually:" -ForegroundColor Yellow
    Write-Host '  az functionapp config appsettings set --resource-group rg-smartstudy-dev --name smartstudy-func --settings "KEY=VALUE"'
}

Write-Host ""
