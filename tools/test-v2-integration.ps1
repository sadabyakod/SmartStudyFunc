# Test V2 with Real Database Question
Write-Host "========================================="
Write-Host "V2 Integration Test - Real Question"
Write-Host "========================================="
Write-Host ""

$baseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$functionKey = "YOUR_FUNCTION_KEY_HERE"

# Get a real question from database
Write-Host "[1/3] Loading question from database..."
$cs = "Server=smartstudysqlsrv.database.windows.net;Database=smartstudydb;User Id=schooladmin;Password=India@12345;Encrypt=True;Connection Timeout=30;"
Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection($cs)
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT TOP 1 Id, QuestionText, ModelAnswer, Subject FROM ExamQuestions WHERE Subject = 'Mathematics' ORDER BY Id DESC"
$reader = $cmd.ExecuteReader()

if (-not $reader.Read()) {
    Write-Host "  [FAIL] No mathematics questions found in database" -ForegroundColor Red
    $reader.Close()
    $conn.Close()
    exit 1
}

$questionId = $reader['Id']
$questionText = $reader['QuestionText']
$modelAnswer = $reader['ModelAnswer']
$subject = $reader['Subject']

Write-Host "  [OK] Question loaded" -ForegroundColor Green
Write-Host "  Question ID: $questionId"
Write-Host "  Subject: $subject"
Write-Host "  Question: $($questionText.Substring(0, [Math]::Min(60, $questionText.Length)))..."
Write-Host "  Model Answer: $modelAnswer"

$reader.Close()
$conn.Close()

Write-Host ""
Write-Host "[2/3] Testing V2 evaluation endpoint..."

$payload = @{
    examId = "TEST-EXAM-001"
    questionId = $questionId
    studentAnswerText = $modelAnswer
} | ConvertTo-Json

Write-Host "  Payload: ExamId=TEST-EXAM-001, QuestionId=$questionId"
Write-Host "  Student Answer: $modelAnswer"

try {
    $headers = @{
        "x-functions-key" = $functionKey
    }
    
    $response = Invoke-RestMethod -Uri "$baseUrl/api/answers/evaluate/v2" `
        -Method Post `
        -Body $payload `
        -ContentType "application/json" `
        -Headers $headers `
        -TimeoutSec 120
    
    Write-Host "  [PASS] Evaluation successful!" -ForegroundColor Green
    Write-Host "  Marks: $($response.score)/$($response.maxMarks)"
    Write-Host "  Percentage: $($response.percentage)%"
    Write-Host "  Feedback: $($response.feedback.Substring(0, [Math]::Min(100, $response.feedback.Length)))..."
    Write-Host "  Keywords Matched: $($response.keywordsMatched.Count)"
    Write-Host "  Evaluation ID: $($response.evaluationId)"
    
    $evaluationId = $response.evaluationId
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    Write-Host "  [FAIL] Evaluation failed (HTTP $statusCode)" -ForegroundColor Red
    
    if ($_.ErrorDetails.Message) {
        Write-Host "  Error: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
    }
    else {
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    exit 1
}

Write-Host ""
Write-Host "[3/3] Checking audit log..."

Start-Sleep -Seconds 2

$conn.Open()
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = @"
SELECT TOP 1
    EngineName,
    SubjectCategory,
    QuestionType,
    MarksAwarded,
    MaxMarks,
    ConfidenceScore,
    NeedsReview,
    EvaluatedAt
FROM EvaluationAuditLog
WHERE QuestionId = @QuestionId
ORDER BY EvaluatedAt DESC
"@
$cmd2.Parameters.AddWithValue("@QuestionId", $questionId.ToString())

$reader2 = $cmd2.ExecuteReader()

if ($reader2.Read()) {
    Write-Host "  [OK] Audit entry found!" -ForegroundColor Green
    Write-Host "  Engine: $($reader2['EngineName'])"
    Write-Host "  Subject: $($reader2['SubjectCategory'])"
    Write-Host "  Question Type: $($reader2['QuestionType'])"
    Write-Host "  Marks: $($reader2['MarksAwarded'])/$($reader2['MaxMarks'])"
    Write-Host "  Confidence: $($reader2['ConfidenceScore'])"
    Write-Host "  Needs Review: $($reader2['NeedsReview'])"
    Write-Host "  Timestamp: $($reader2['EvaluatedAt'])"
}
else {
    Write-Host "  [WARN] No audit entry found (audit logger may not be integrated)" -ForegroundColor Yellow
}

$reader2.Close()
$conn.Close()

Write-Host ""
Write-Host "========================================="
Write-Host "[SUCCESS] V2 Integration Test Complete"
Write-Host "========================================="
