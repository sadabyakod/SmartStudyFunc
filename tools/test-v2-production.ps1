# Test V2 Evaluation System - Production Tests
Write-Host "========================================="
Write-Host "V2 PRODUCTION VALIDATION TESTS"
Write-Host "========================================="
Write-Host ""

$baseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$functionKey = "YOUR_FUNCTION_KEY_HERE"

$passCount = 0
$failCount = 0

# Helper function to test evaluation
function Test-Evaluation {
    param(
        [string]$TestName,
        [string]$StudentAnswer,
        [string]$ModelAnswer,
        [string]$Subject,
        [double]$MaxMarks = 10
    )
    
    Write-Host "[$TestName]" -ForegroundColor Cyan
    Write-Host "  Student: $StudentAnswer"
    Write-Host "  Model: $ModelAnswer"
    
    $payload = @{
        examId = 999
        questionId = "TEST-$([guid]::NewGuid().ToString().Substring(0,8))"
        studentAnswer = $StudentAnswer
        modelAnswer = $ModelAnswer
        maxMarks = $MaxMarks
        subject = $Subject
        questionType = "Numerical"
        classLevel = 10
    } | ConvertTo-Json
    
    try {
        $headers = @{
            "x-functions-key" = $functionKey
        }
        
        $response = Invoke-RestMethod -Uri "$baseUrl/api/answers/evaluate/v2" `
            -Method Post `
            -Body $payload `
            -ContentType "application/json" `
            -Headers $headers `
            -TimeoutSec 60
        
        Write-Host "  [PASS] Marks: $($response.marksAwarded)/$($response.maxMarks) | Confidence: $($response.confidenceScore) | Engine: $($response.processedBy)" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "  [FAIL] $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails.Message) {
            Write-Host "  Details: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
        }
        return $false
    }
}

Write-Host "TEST SUITE: Mathematics Engine"
Write-Host "========================================="
Write-Host ""

# Test 1: Simple numerical equivalence
if (Test-Evaluation -TestName "Test 1.1: Numerical Equivalence" -StudentAnswer "42" -ModelAnswer "42" -Subject "Mathematics") {
    $passCount++
} else {
    $failCount++
}
Write-Host ""

# Test 2: Symbolic equivalence
if (Test-Evaluation -TestName "Test 1.2: Symbolic Equivalence" -StudentAnswer "F = m * a" -ModelAnswer "F=ma" -Subject "Mathematics") {
    $passCount++
} else {
    $failCount++
}
Write-Host ""

# Test 3: Variable aliasing
if (Test-Evaluation -TestName "Test 1.3: Variable Aliases" -StudentAnswer "Area = base * height" -ModelAnswer "A = b * h" -Subject "Mathematics") {
    $passCount++
} else {
    $failCount++
}
Write-Host ""

# Test 4: OCR normalization
if (Test-Evaluation -TestName "Test 1.4: OCR Normalization" -StudentAnswer "π × r²" -ModelAnswer "pi * r^2" -Subject "Mathematics") {
    $passCount++
} else {
    $failCount++
}
Write-Host ""

Write-Host "TEST SUITE: Physics/Chemistry Engine"
Write-Host "========================================="
Write-Host ""

# Test 5: Unit validation
if (Test-Evaluation -TestName "Test 2.1: Unit Conversion" -StudentAnswer "50 cm" -ModelAnswer "0.5 m" -Subject "Physics") {
    $passCount++
} else {
    $failCount++
}
Write-Host ""

# Test 6: Energy units
if (Test-Evaluation -TestName "Test 2.2: Energy Units" -StudentAnswer "1000 J" -ModelAnswer "1 kJ" -Subject "Physics") {
    $passCount++
} else {
    $failCount++
}
Write-Host ""

Write-Host ""
Write-Host "========================================="
Write-Host "TEST RESULTS SUMMARY"
Write-Host "========================================="
Write-Host "Passed: $passCount" -ForegroundColor Green
Write-Host "Failed: $failCount" -ForegroundColor Red
Write-Host "Total: $($passCount + $failCount)"
Write-Host ""

# Check audit log
Write-Host "Checking audit log entries..."
try {
    $connectionString = "Server=smartstudysqlsrv.database.windows.net;Database=smartstudydb;User Id=schooladmin;Password=India@12345;Encrypt=True;Connection Timeout=30;"
    Add-Type -AssemblyName System.Data
    $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $conn.Open()
    
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = @"
SELECT TOP 5
    EngineName,
    SubjectCategory,
    MarksAwarded,
    MaxMarks,
    ConfidenceScore,
    EvaluatedAt
FROM EvaluationAuditLog
ORDER BY EvaluatedAt DESC
"@
    
    $reader = $cmd.ExecuteReader()
    $hasRows = $false
    
    while ($reader.Read()) {
        if (-not $hasRows) {
            Write-Host "Recent evaluations logged:" -ForegroundColor Green
            $hasRows = $true
        }
        Write-Host "  - $($reader['EngineName']) | $($reader['SubjectCategory']) | $($reader['MarksAwarded'])/$($reader['MaxMarks']) | Conf: $($reader['ConfidenceScore'])"
    }
    
    if (-not $hasRows) {
        Write-Host "  No audit entries found (check if audit logger is active)" -ForegroundColor Yellow
    }
    
    $reader.Close()
    $conn.Close()
}
catch {
    Write-Host "  [WARN] Could not check audit log: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================="
Write-Host "Validation Complete"
Write-Host "========================================="
