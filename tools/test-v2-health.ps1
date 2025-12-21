# Test V2 Evaluation System - Health Check
Write-Host "=================================="
Write-Host "V2 System Health Check"
Write-Host "=================================="
Write-Host ""

$baseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"

# Test 1: Health endpoint
Write-Host "[1/3] Testing health endpoint..."
try {
    $health = Invoke-RestMethod -Uri "$baseUrl/api/health" -Method Get -TimeoutSec 30
    Write-Host "  [OK] Health endpoint responding" -ForegroundColor Green
    Write-Host "  Status: $($health.status)"
}
catch {
    Write-Host "  [FAIL] Health check failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "[2/3] Testing DI container registration..."

# Test 2: Simple evaluation to verify DI container
$testPayload = @{
    examId = 999
    questionId = "TEST-001"
    studentAnswer = "42"
    modelAnswer = "42"
    maxMarks = 10
    subject = "Mathematics"
    questionType = "Numerical"
    classLevel = 10
} | ConvertTo-Json

Write-Host "  Payload: Mathematics evaluation test"
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/answers/evaluate/v2" -Method Post -Body $testPayload -ContentType "application/json" -TimeoutSec 60
    Write-Host "  [OK] V2 evaluation endpoint responding" -ForegroundColor Green
    Write-Host "  Engine: $($response.processedBy)"
    Write-Host "  Marks: $($response.marksAwarded)/$($response.maxMarks)"
    Write-Host "  Confidence: $($response.confidenceScore)"
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $errorBody = $_.ErrorDetails.Message
    Write-Host "  [FAIL] V2 evaluation failed" -ForegroundColor Red
    Write-Host "  Status Code: $statusCode"
    Write-Host "  Error: $errorBody"
}

Write-Host ""
Write-Host "[3/3] Testing database audit logging..."

# Test 3: Check if audit log table is accessible
try {
    $connectionString = "Server=smartstudysqlsrv.database.windows.net;Database=smartstudydb;User Id=schooladmin;Password=India@12345;Encrypt=True;Connection Timeout=30;"
    Add-Type -AssemblyName System.Data
    $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $conn.Open()
    
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM EvaluationAuditLog WHERE EvaluatedAt >= DATEADD(minute, -5, GETUTCDATE())"
    $recentCount = $cmd.ExecuteScalar()
    
    Write-Host "  [OK] Audit log table accessible" -ForegroundColor Green
    Write-Host "  Recent evaluations (last 5 min): $recentCount"
    
    $conn.Close()
}
catch {
    Write-Host "  [FAIL] Audit log check failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=================================="
Write-Host "Health Check Complete"
Write-Host "=================================="
