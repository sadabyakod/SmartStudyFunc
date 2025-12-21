# End-to-End Answer Sheet Evaluation Test
$ErrorActionPreference = "Stop"

$baseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$key = "YOUR_FUNCTION_KEY_HERE"
$imagePath = "C:\SmartStudyFunc\test-answer-sheet.jpg"
$examId = 12345
$questionId = 1

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ANSWER SHEET EVALUATION - END TO END" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check image
Write-Host "[1/4] Checking image file..." -ForegroundColor White
if (!(Test-Path $imagePath)) {
    Write-Host "  ERROR: Image not found at $imagePath" -ForegroundColor Red
    exit 1
}
$fileSize = (Get-Item $imagePath).Length
Write-Host "  OK: Image found ($($fileSize/1KB) KB)" -ForegroundColor Green
Write-Host ""

# Step 2: Upload and OCR
Write-Host "[2/4] Uploading answer sheet and extracting text..." -ForegroundColor White
try {
    $form = @{
        examId = $examId
        questionId = $questionId
        file = Get-Item $imagePath
    }
    
    $uploadResponse = Invoke-RestMethod -Uri "$baseUrl/api/answers/upload" -Method Post -Form $form -Headers @{"x-functions-key"=$key}
    
    Write-Host "  OK: Upload successful!" -ForegroundColor Green
    Write-Host "  Extracted: $($uploadResponse.extractedText.Length) characters" -ForegroundColor Cyan
    Write-Host "  Blob: $($uploadResponse.blobPath)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  --- EXTRACTED TEXT ---" -ForegroundColor Gray
    Write-Host $uploadResponse.extractedText -ForegroundColor White
    Write-Host "  --- END ---" -ForegroundColor Gray
    Write-Host ""
    
    $extractedText = $uploadResponse.extractedText
    $blobPath = $uploadResponse.blobPath
    
} catch {
    Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host "  Details: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
    exit 1
}

# Step 3: Evaluate
Write-Host "[3/4] Evaluating answer with AI..." -ForegroundColor White
try {
    $evalBody = @{
        examId = $examId
        questionId = $questionId
        studentAnswerText = $extractedText
        expectedAnswer = "Solve the calculus differentiation problem step-by-step using chain rule and product rule. Show all intermediate steps and simplify the final answer."
        maxMarks = 20
    } | ConvertTo-Json
    
    $evalResponse = Invoke-RestMethod -Uri "$baseUrl/api/answers/evaluate" -Method Post -Body $evalBody -ContentType "application/json" -Headers @{"x-functions-key"=$key}
    
    Write-Host "  OK: Evaluation complete!" -ForegroundColor Green
    Write-Host ""
    
} catch {
    Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host "  Details: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
    exit 1
}

# Step 4: Display Results
Write-Host "[4/4] EVALUATION RESULTS" -ForegroundColor White
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Evaluation ID: $($evalResponse.evaluationId)" -ForegroundColor White
Write-Host "Exam ID: $($evalResponse.examId)" -ForegroundColor White
Write-Host "Question ID: $($evalResponse.questionId)" -ForegroundColor White
Write-Host ""

$percentage = $evalResponse.percentage
$color = if ($percentage -ge 70) { "Green" } elseif ($percentage -ge 40) { "Yellow" } else { "Red" }
Write-Host "SCORE: $($evalResponse.score) / $($evalResponse.maxMarks) ($percentage%)" -ForegroundColor $color
Write-Host "Status: $(if($evalResponse.isComplete){'Complete'}else{'Incomplete'})" -ForegroundColor $(if($evalResponse.isComplete){"Green"}else{"Yellow"})
Write-Host ""

# Step-wise breakdown
if ($evalResponse.stepWiseBreakdown -and $evalResponse.stepWiseBreakdown.Count -gt 0) {
    Write-Host "--- STEP-WISE BREAKDOWN ---" -ForegroundColor Yellow
    foreach ($step in $evalResponse.stepWiseBreakdown) {
        $icon = if ($step.marksAwarded -eq $step.maxMarks) { "[OK]" } elseif ($step.marksAwarded -gt 0) { "[~]" } else { "[X]" }
        Write-Host "$icon Step $($step.stepNumber): $($step.stepDescription)" -ForegroundColor White
        Write-Host "    Marks: $($step.marksAwarded)/$($step.maxMarks)" -ForegroundColor Cyan
        Write-Host "    Feedback: $($step.feedback)" -ForegroundColor Gray
        Write-Host ""
    }
}

# Overall feedback
Write-Host "--- OVERALL FEEDBACK ---" -ForegroundColor Magenta
Write-Host $evalResponse.feedback -ForegroundColor White
Write-Host ""

if ($evalResponse.strengths) {
    Write-Host "Strengths:" -ForegroundColor Green
    Write-Host $evalResponse.strengths -ForegroundColor White
    Write-Host ""
}

if ($evalResponse.improvements) {
    Write-Host "Areas for Improvement:" -ForegroundColor Yellow
    Write-Host $evalResponse.improvements -ForegroundColor White
    Write-Host ""
}

# Keywords analysis
if ($evalResponse.keywordsMatched -and $evalResponse.keywordsMatched.Count -gt 0) {
    Write-Host "Concepts Covered: " -NoNewline -ForegroundColor Green
    Write-Host ($evalResponse.keywordsMatched -join ", ") -ForegroundColor White
}

if ($evalResponse.missingKeywords -and $evalResponse.missingKeywords.Count -gt 0) {
    Write-Host "Missing Concepts: " -NoNewline -ForegroundColor Red
    Write-Host ($evalResponse.missingKeywords -join ", ") -ForegroundColor White
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "TEST COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

# Save results
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultsFile = "C:\SmartStudyFunc\evaluation-results-$timestamp.json"
$evalResponse | ConvertTo-Json -Depth 10 | Out-File $resultsFile
Write-Host "Results saved to: $resultsFile" -ForegroundColor Cyan
