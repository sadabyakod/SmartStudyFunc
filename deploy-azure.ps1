# Azure Function App Deployment Script
# SmartStudy Functions - Production Deployment
# =============================================

param(
    [switch]$SkipBuild,
    [switch]$PublishOnly
)

# Azure Configuration
$SubscriptionId = "64cf6114-e23f-4507-ba2f-7bf5b133a9fe"
$ResourceGroup = "rg-smartstudy-dev"
$FunctionAppName = "smartstudy-func"
$Location = "centralindia"
$FunctionUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"

# Colors for output
function Write-Success { param($msg) Write-Host "✅ $msg" -ForegroundColor Green }
function Write-Info { param($msg) Write-Host "ℹ️  $msg" -ForegroundColor Cyan }
function Write-Warning { param($msg) Write-Host "⚠️  $msg" -ForegroundColor Yellow }
function Write-Error { param($msg) Write-Host "❌ $msg" -ForegroundColor Red }

Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║         SmartStudy Functions - Azure Deployment            ║" -ForegroundColor Magenta
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""

# Step 1: Check Azure CLI login
Write-Info "Checking Azure CLI login status..."
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Warning "Not logged in to Azure CLI. Logging in..."
    az login
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Azure login failed. Please run 'az login' manually."
        exit 1
    }
}
Write-Success "Logged in as: $($account.user.name)"

# Step 2: Set subscription
Write-Info "Setting subscription to: $SubscriptionId"
az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to set subscription"
    exit 1
}
Write-Success "Subscription set successfully"

# Step 3: Build the project
if (-not $SkipBuild -and -not $PublishOnly) {
    Write-Info "Building project..."
    dotnet clean
    dotnet restore
    dotnet build --configuration Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }
    Write-Success "Build completed"
}

# Step 4: Publish the project
Write-Info "Publishing project..."
$publishFolder = ".\bin\publish"
if (Test-Path $publishFolder) {
    Remove-Item -Path $publishFolder -Recurse -Force
}
dotnet publish --configuration Release --output $publishFolder
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed"
    exit 1
}
Write-Success "Publish completed to: $publishFolder"

# Step 5: Deploy to Azure
Write-Info "Deploying to Azure Function App: $FunctionAppName..."

# Create zip package
$zipPath = ".\bin\publish.zip"
if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}
Compress-Archive -Path "$publishFolder\*" -DestinationPath $zipPath -Force
Write-Success "Created deployment package: $zipPath"

# Deploy using zip deploy
Write-Info "Uploading to Azure..."
az functionapp deployment source config-zip `
    --resource-group $ResourceGroup `
    --name $FunctionAppName `
    --src $zipPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Deployment failed"
    exit 1
}

Write-Success "Deployment completed successfully!"

# Step 6: Verify deployment
Write-Info "Verifying deployment..."
Start-Sleep -Seconds 5

# Check health endpoint
$healthUrl = "$FunctionUrl/api/health"
Write-Info "Checking health endpoint: $healthUrl"
try {
    $response = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 30
    Write-Success "Health check passed: $($response.status)"
} catch {
    Write-Warning "Health check could not be verified (this is normal for cold starts)"
}

# Display deployment info
Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║              Deployment Summary                            ║" -ForegroundColor Green
Write-Host "╠════════════════════════════════════════════════════════════╣" -ForegroundColor Green
Write-Host "║ Function App:  $FunctionAppName" -ForegroundColor Green
Write-Host "║ Resource Group: $ResourceGroup" -ForegroundColor Green
Write-Host "║ Location:      $Location" -ForegroundColor Green
Write-Host "║ URL:           $FunctionUrl" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

# List deployed functions
Write-Info "Fetching deployed functions..."
az functionapp function list --resource-group $ResourceGroup --name $FunctionAppName --output table

Write-Host ""
Write-Success "Deployment complete! Your functions are now live at:"
Write-Host "   $FunctionUrl/api/function-name" -ForegroundColor Cyan
Write-Host ""
