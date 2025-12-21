# Simple Text-Based Evaluation Test (No Image Upload)
$ErrorActionPreference = "Stop"

$baseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$key = "YOUR_FUNCTION_KEY_HERE"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TEXT-BASED ANSWER EVALUATION TEST" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test with calculus problem (matching the image content)
$studentAnswer = @"
Part-B (5 marks)
Question: Find dy/dx for the given function

Solution:
Step 1: Given function y = (x^2 + 3x)(e^x)
Step 2: Using product rule: d/dx(uv) = u(dv/dx) + v(du/dx)
Step 3: Let u = x^2 + 3x, then du/dx = 2x + 3
Step 4: Let v = e^x, then dv/dx = e^x
Step 5: dy/dx = (x^2 + 3x)(e^x) + (e^x)(2x + 3)
Step 6: dy/dx = e^x(x^2 + 3x + 2x + 3)
Step 7: Final answer: dy/dx = e^x(x^2 + 5x + 3)
"@

$expectedAnswer = @"
Use the product rule for differentiation: d/dx(uv) = u(dv/dx) + v(du/dx).
Differentiate each part correctly.
Simplify the final expression by factoring out e^x.
Show all intermediate steps clearly.
"@

Write-Host "[1/2] Submitting answer for evaluation..." -ForegroundColor White

$questionGuid = [Guid]::NewGuid().ToString()
Write-Host "  Using Question ID: $questionGuid" -ForegroundColor Gray

try {
    $evalBody = @{
        examId = "EXAM_12345"
        questionId = $questionGuid
        studentAnswerText = $studentAnswer
    } | ConvertTo-Json
    
    Write-Host "  Calling API..." -ForegroundColor Gray
    $evalResponse = Invoke-RestMethod -Uri "$baseUrl/api/answers/evaluate" `
        -Method Post `
        -Body $evalBody `
        -ContentType "application/json" `
        -Headers @{"x-functions-key"=$key}
    
    Write-Host "  OK: Evaluation complete!" -ForegroundColor Green
    Write-Host ""
    
} catch {
    Write-Host "  ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host "  Details: $($_.ErrorDetails.Message)" -ForegroundColor Red
    }
    if ($_.Exception.Response) {
        Write-Host "  Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Yellow
    }
    exit 1
}

# Display Results
Write-Host "[2/2] EVALUATION RESULTS" -ForegroundColor White
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
    Write-Host ""
    foreach ($step in $evalResponse.stepWiseBreakdown) {
        $icon = if ($step.marksAwarded -eq $step.maxMarks) { "[FULL]" } elseif ($step.marksAwarded -gt 0) { "[PART]" } else { "[MISS]" }
        $stepColor = if ($step.marksAwarded -eq $step.maxMarks) { "Green" } elseif ($step.marksAwarded -gt 0) { "Yellow" } else { "Red" }
        
        Write-Host "$icon Step $($step.stepNumber): $($step.stepDescription)" -ForegroundColor $stepColor
        Write-Host "     Marks Awarded: $($step.marksAwarded) / $($step.maxMarks)" -ForegroundColor Cyan
        Write-Host "     Feedback: $($step.feedback)" -ForegroundColor Gray
        Write-Host ""
    }
}

# Overall feedback
Write-Host "--- OVERALL FEEDBACK ---" -ForegroundColor Magenta
Write-Host $evalResponse.feedback -ForegroundColor White
Write-Host ""

if ($evalResponse.strengths) {
    Write-Host "STRENGTHS:" -ForegroundColor Green
    Write-Host $evalResponse.strengths -ForegroundColor White
    Write-Host ""
}

if ($evalResponse.improvements) {
    Write-Host "AREAS FOR IMPROVEMENT:" -ForegroundColor Yellow
    Write-Host $evalResponse.improvements -ForegroundColor White
    Write-Host ""
}

# Keywords analysis
if ($evalResponse.keywordsMatched -and $evalResponse.keywordsMatched.Count -gt 0) {
    Write-Host "CONCEPTS COVERED: " -NoNewline -ForegroundColor Green
    Write-Host ($evalResponse.keywordsMatched -join ", ") -ForegroundColor White
}

if ($evalResponse.missingKeywords -and $evalResponse.missingKeywords.Count -gt 0) {
    Write-Host "MISSING CONCEPTS: " -NoNewline -ForegroundColor Red
    Write-Host ($evalResponse.missingKeywords -join ", ") -ForegroundColor White
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "TEST COMPLETED!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

# Save results
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultsFile = "C:\SmartStudyFunc\evaluation-text-test-$timestamp.json"
$evalResponse | ConvertTo-Json -Depth 10 | Out-File $resultsFile
Write-Host "Results saved to: $resultsFile" -ForegroundColor Cyan
Write-Host ""
