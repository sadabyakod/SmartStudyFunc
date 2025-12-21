# Complete Evaluation Test - Generate Question, Answer, and Get Feedback
$ErrorActionPreference = "Stop"

$BaseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$FunctionKey = "YOUR_FUNCTION_KEY_HERE"
$ConnectionString = "Server=tcp:smartstudysqlsrv.database.windows.net,1433;Initial Catalog=smartstudydb;User ID=schooladmin;Password=India@12345;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

Write-Host "`n======================================================" -ForegroundColor Cyan
Write-Host "  COMPLETE EVALUATION TEST WITH FEEDBACK" -ForegroundColor Yellow
Write-Host "======================================================" -ForegroundColor Cyan

# ========================================
# STEP 1: Generate New Question
# ========================================
Write-Host "`n[STEP 1] Generating new question..." -ForegroundColor White

$QuestionId = [Guid]::NewGuid().ToString()
$ExamId = "EXAM-" + (Get-Date -Format "yyyyMMdd-HHmmss")

$questionText = "Solve the following quadratic equation using the quadratic formula: 2x^2 + 5x - 3 = 0. Show all steps of your work."

$modelAnswer = "Step 1: Identify coefficients - a = 2, b = 5, c = -3. Step 2: Write quadratic formula - x = (-b +/- sqrt(b^2 - 4ac)) / (2a). Step 3: Calculate discriminant - Delta = 5^2 - 4(2)(-3) = 25 + 24 = 49. Step 4: Apply quadratic formula - x = (-5 +/- sqrt(49)) / 4 = (-5 +/- 7) / 4. Step 5: Find two solutions - x1 = (-5 + 7)/4 = 2/4 = 0.5 and x2 = (-5 - 7)/4 = -12/4 = -3. Final Answer: x = 0.5 or x = -3"

$keywords = "quadratic formula, discriminant, coefficients, a=2, b=5, c=-3, sqrt(49), x=0.5, x=-3, two solutions"

$SqlQuery = @"
INSERT INTO ExamQuestions (
    Id, ExamId, QuestionNumber, QuestionText, MaxScore, ModelAnswer, Keywords, QuestionType, CreatedAt
)
VALUES (
    '$QuestionId',
    '$ExamId',
    1,
    '$($questionText -replace "'", "''")',
    20,
    '$($modelAnswer -replace "'", "''")',
    '$keywords',
    'descriptive',
    GETUTCDATE()
);
"@

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $ConnectionString
    $connection.Open()
    
    $command = $connection.CreateCommand()
    $command.CommandText = $SqlQuery
    $command.ExecuteNonQuery() | Out-Null
    
    $connection.Close()
    
    Write-Host "PASS Question created successfully!" -ForegroundColor Green
    Write-Host "  Question ID: $QuestionId" -ForegroundColor Cyan
    Write-Host "  Exam ID: $ExamId" -ForegroundColor Cyan
    Write-Host "  Max Score: 20 marks" -ForegroundColor Yellow
} catch {
    Write-Host "FAIL Failed to create question: $_" -ForegroundColor Red
    exit 1
}

# ========================================
# STEP 2: Generate Student Answer
# ========================================
Write-Host "`n[STEP 2] Generating student answer sheet..." -ForegroundColor White

$studentAnswer = "Solution: Step 1: Find coefficients - a = 2, b = 5, c = -3. Step 2: Quadratic formula - x = (-b +/- sqrt(b^2 - 4ac)) / 2a. Step 3: Discriminant - Delta = 5^2 - 4(2)(-3) = 25 + 24 = 49. Step 4: Apply formula - x = (-5 +/- sqrt(49)) / 4 = (-5 +/- 7) / 4. Step 5: Two answers - x1 = (-5 + 7)/4 = 2/4 = 0.5 and x2 = (-5 - 7)/4 = -12/4 = -3. Answer: x = 0.5 or x = -3"

Write-Host "PASS Student answer generated!" -ForegroundColor Green
Write-Host "`nStudent's Answer:" -ForegroundColor Cyan
Write-Host $studentAnswer -ForegroundColor Gray

# ========================================
# STEP 3: Call Evaluation API
# ========================================
Write-Host "`n[STEP 3] Calling evaluation API..." -ForegroundColor White

$evaluationRequest = @{
    examId = $ExamId
    questionId = $QuestionId
    studentAnswerText = $studentAnswer
} | ConvertTo-Json

Write-Host "  Sending to: $BaseUrl/api/answers/evaluate" -ForegroundColor Gray

try {
    Write-Host "Request body:" -ForegroundColor Gray
    Write-Host $evaluationRequest -ForegroundColor DarkGray
    
    $evaluationResponse = Invoke-RestMethod -Uri "$BaseUrl/api/answers/evaluate?code=$FunctionKey" -Method Post -ContentType "application/json" -Body $evaluationRequest -TimeoutSec 60
    
    Write-Host "PASS Evaluation completed!" -ForegroundColor Green
    
} catch {
    Write-Host "FAIL Evaluation failed: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        try {
            $stream = $_.Exception.Response.GetResponseStream()
            $stream.Position = 0
            $reader = New-Object System.IO.StreamReader($stream)
            $responseBody = $reader.ReadToEnd()
            Write-Host "Response: $responseBody" -ForegroundColor Gray
        } catch {
            Write-Host "Could not read error response" -ForegroundColor Gray
        }
    }
    
    exit 1
}

# ========================================
# STEP 4: Display Feedback with Marks
# ========================================
Write-Host "`n======================================================" -ForegroundColor Cyan
Write-Host "  EVALUATION RESULTS & FEEDBACK" -ForegroundColor Yellow
Write-Host "======================================================" -ForegroundColor Cyan

Write-Host "`nOVERALL SCORE" -ForegroundColor Yellow
Write-Host "   Marks Awarded: $($evaluationResponse.awardedScore) / $($evaluationResponse.maxScore)" -ForegroundColor White
$percentage = [Math]::Round(($evaluationResponse.awardedScore / $evaluationResponse.maxScore) * 100, 1)
Write-Host "   Percentage: $percentage%" -ForegroundColor Cyan

if ($percentage -ge 90) {
    Write-Host "   Grade: A+ (Excellent)" -ForegroundColor Green
} elseif ($percentage -ge 80) {
    Write-Host "   Grade: A (Very Good)" -ForegroundColor Green
} elseif ($percentage -ge 70) {
    Write-Host "   Grade: B (Good)" -ForegroundColor Yellow
} elseif ($percentage -ge 60) {
    Write-Host "   Grade: C (Satisfactory)" -ForegroundColor Yellow
} else {
    Write-Host "   Grade: D (Needs Improvement)" -ForegroundColor Red
}

Write-Host "`nDETAILED FEEDBACK" -ForegroundColor Yellow
Write-Host $evaluationResponse.feedback -ForegroundColor White

if ($evaluationResponse.stepWiseScores) {
    Write-Host "`nSTEP-WISE BREAKDOWN" -ForegroundColor Yellow
    $stepNum = 1
    foreach ($step in $evaluationResponse.stepWiseScores) {
        $stepMarks = if ($step.marksAwarded) { $step.marksAwarded } else { $step.marks }
        Write-Host "   Step ${stepNum}: $stepMarks marks - $($step.description)" -ForegroundColor Cyan
        $stepNum++
    }
}

if ($evaluationResponse.correctKeywords -or $evaluationResponse.missingKeywords) {
    Write-Host "`nKEYWORD ANALYSIS" -ForegroundColor Yellow
    
    if ($evaluationResponse.correctKeywords) {
        Write-Host "   PASS Correct keywords found:" -ForegroundColor Green
        foreach ($keyword in $evaluationResponse.correctKeywords) {
            Write-Host "     - $keyword" -ForegroundColor White
        }
    }
    
    if ($evaluationResponse.missingKeywords) {
        Write-Host "   FAIL Missing keywords:" -ForegroundColor Red
        foreach ($keyword in $evaluationResponse.missingKeywords) {
            Write-Host "     - $keyword" -ForegroundColor White
        }
    }
}

if ($evaluationResponse.suggestions) {
    Write-Host "`nSUGGESTIONS FOR IMPROVEMENT" -ForegroundColor Yellow
    Write-Host $evaluationResponse.suggestions -ForegroundColor White
}

Write-Host "`n======================================================" -ForegroundColor Cyan
Write-Host "  TEST COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Cyan

Write-Host "`nSummary:" -ForegroundColor Yellow
Write-Host "   Question ID: $QuestionId" -ForegroundColor Cyan
Write-Host "   Exam ID: $ExamId" -ForegroundColor Cyan
Write-Host "   Total Marks: $($evaluationResponse.awardedScore)/$($evaluationResponse.maxScore)" -ForegroundColor White
Write-Host "   Status: Evaluation Complete" -ForegroundColor Green
Write-Host ""
