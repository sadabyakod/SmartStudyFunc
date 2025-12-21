# Check V2 Service Registration
Write-Host "========================================="
Write-Host "V2 DI Container Diagnostic"
Write-Host "========================================="
Write-Host ""

Write-Host "Checking Program.cs for V2 service registrations..."
Write-Host ""

$programCs = Get-Content "C:\SmartStudyFunc\Program.cs" -Raw

$services = @(
    "AddMemoryCache",
    "EnhancedQuestionClassifier",
    "SyllabusCacheService",
    "EvaluationAuditLogger"
)

foreach ($service in $services) {
    if ($programCs -match $service) {
        Write-Host "  [OK] $service registered" -ForegroundColor Green
    }
    else {
        Write-Host "  [MISSING] $service NOT registered" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Checking if files exist..."
Write-Host ""

$files = @{
    "EnhancedQuestionClassifier" = "C:\SmartStudyFunc\Services\Evaluation\EnhancedQuestionClassifier.cs"
    "MathEvaluationHelpers" = "C:\SmartStudyFunc\Services\Evaluation\MathEvaluationHelpers.cs"
    "UnitValidationHelpers" = "C:\SmartStudyFunc\Services\Evaluation\UnitValidationHelpers.cs"
    "SyllabusCacheService" = "C:\SmartStudyFunc\Services\Evaluation\SyllabusCacheService.cs"
    "EvaluationAuditLogger" = "C:\SmartStudyFunc\Services\Evaluation\EvaluationAuditLogger.cs"
}

foreach ($file in $files.GetEnumerator()) {
    if (Test-Path $file.Value) {
        Write-Host "  [OK] $($file.Key) exists" -ForegroundColor Green
    }
    else {
        Write-Host "  [MISSING] $($file.Key) NOT found" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Checking deployment package..."
Write-Host ""

if (Test-Path "C:\SmartStudyFunc\bin\publish") {
    $publishedFiles = Get-ChildItem "C:\SmartStudyFunc\bin\publish" -Filter "*.dll" -Recurse
    $mainDll = $publishedFiles | Where-Object { $_.Name -eq "SmartStudyFunc.dll" }
    
    if ($mainDll) {
        Write-Host "  [OK] SmartStudyFunc.dll found in publish folder" -ForegroundColor Green
        Write-Host "  Path: $($mainDll.FullName)"
        Write-Host "  Size: $([Math]::Round($mainDll.Length / 1MB, 2)) MB"
        Write-Host "  Modified: $($mainDll.LastWriteTime)"
    }
}

Write-Host ""
Write-Host "========================================="
