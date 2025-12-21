# ═══════════════════════════════════════════════════════════
# FULL ANSWER SHEET EVALUATION FLOW - END TO END TEST
# ═══════════════════════════════════════════════════════════
# This script tests:
# 1. Upload answer sheet image
# 2. OCR text extraction (automatic)
# 3. Evaluation with AI
# 4. Progress tracking
# 5. Final results with step-wise marks
# ═══════════════════════════════════════════════════════════

param(
    [string]$ImagePath = "C:\SmartStudyFunc\test-answer-sheet.jpg",
    [string]$ExamId = "MATH-TEST-001",
    [string]$QuestionId = "Q1",
    [string]$FunctionKey = "YOUR_FUNCTION_KEY_HERE"
)

$BaseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net/api"

# Helper function for colored output
function Write-Step {
    param([string]$Message, [string]$Status = "INFO")
    $timestamp = Get-Date -Format "HH:mm:ss"
    switch ($Status) {
        "SUCCESS" { Write-Host "[$timestamp] $Message" -ForegroundColor Green }
        "ERROR" { Write-Host "[$timestamp] $Message" -ForegroundColor Red }
        "WARNING" { Write-Host "[$timestamp] $Message" -ForegroundColor Yellow }
        "INFO" { Write-Host "[$timestamp] $Message" -ForegroundColor Cyan }
        "STEP" { Write-Host "`n[$timestamp] $Message" -ForegroundColor Magenta }
        "PROGRESS" { Write-Host "[$timestamp] $Message" -ForegroundColor Blue }
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor White
Write-Host "   FULL ANSWER SHEET EVALUATION FLOW - END TO END TEST     " -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor White
Write-Host ""

# ═══════════════════════════════════════════════════════════
# STEP 0: Verify Prerequisites
# ═══════════════════════════════════════════════════════════
Write-Step "STEP 0: Verifying Prerequisites" "STEP"

if (-not (Test-Path $ImagePath)) {
    Write-Step "Image file not found at: $ImagePath" "ERROR"
    Write-Host "Please save your answer sheet image to this path and try again." -ForegroundColor Yellow
    exit 1
}

$fileInfo = Get-Item $ImagePath
Write-Step "Image found: $($fileInfo.Name) (Size: $([math]::Round($fileInfo.Length/1KB, 2)) KB)" "SUCCESS"

# ═══════════════════════════════════════════════════════════
# STEP 1: Health Check
# ═══════════════════════════════════════════════════════════
Write-Step "STEP 1: Health Check" "STEP"

try {
    $health = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get -TimeoutSec 30
    Write-Step "Function App Status: $($health.status)" "SUCCESS"
    Write-Step "Database: $($health.database)" "INFO"
    Write-Step "OpenAI: $(if($health.openai_configured){'Configured'}else{'Not Configured'})" "INFO"
} catch {
    Write-Step "Health check failed: $($_.Exception.Message)" "ERROR"
    exit 1
}

# ═══════════════════════════════════════════════════════════
# STEP 2: Upload Answer Sheet
# ═══════════════════════════════════════════════════════════
Write-Step "STEP 2: Uploading Answer Sheet and OCR Extraction" "STEP"

try {
    # Read file as bytes
    $fileBin = [System.IO.File]::ReadAllBytes($ImagePath)
    $fileName = [System.IO.Path]::GetFileName($ImagePath)
    $boundary = [System.Guid]::NewGuid().ToString()
    
    # Build multipart form data
    $LF = "`r`n"
    $bodyLines = (
        "--$boundary",
        "Content-Disposition: form-data; name=`"examId`"$LF",
        $ExamId,
        "--$boundary",
        "Content-Disposition: form-data; name=`"questionId`"$LF",
        $QuestionId,
        "--$boundary",
        "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"",
        "Content-Type: image/jpeg$LF",
        [System.Text.Encoding]::GetEncoding("iso-8859-1").GetString($fileBin),
        "--$boundary--$LF"
    ) -join $LF
    
    $uploadStartTime = Get-Date
    Write-Step "Uploading $fileName to Azure Blob Storage..." "PROGRESS"
    
    $uploadResponse = Invoke-RestMethod `
        -Uri "$BaseUrl/answers/upload?code=$FunctionKey" `
        -Method POST `
        -ContentType "multipart/form-data; boundary=$boundary" `
        -Body ([System.Text.Encoding]::GetEncoding("iso-8859-1").GetBytes($bodyLines)) `
        -TimeoutSec 120
    
    $uploadDuration = (Get-Date) - $uploadStartTime
    
    Write-Step "Upload completed in $([math]::Round($uploadDuration.TotalSeconds, 2)) seconds" "SUCCESS"
    Write-Step "OCR extracted $($uploadResponse.extractedLength) characters" "SUCCESS"
    Write-Step "Blob path: $($uploadResponse.blobPath)" "INFO"
    
    Write-Host ""
    Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Gray
    Write-Host "║              EXTRACTED TEXT (OCR Result)              ║" -ForegroundColor Gray
    Write-Host "╠═══════════════════════════════════════════════════════════╣" -ForegroundColor Gray
    Write-Host $uploadResponse.extractedText -ForegroundColor White
    Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Gray
    Write-Host ""
    
    $extractedText = $uploadResponse.extractedText
    $blobPath = $uploadResponse.blobPath
    
} catch {
    Write-Step "Upload/OCR failed: $($_.Exception.Message)" "ERROR"
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $errorBody = $reader.ReadToEnd()
        Write-Host "Error details: $errorBody" -ForegroundColor Red
    }
    exit 1
}

# ═══════════════════════════════════════════════════════════
# STEP 3: Evaluate Answer with AI
# ═══════════════════════════════════════════════════════════
Write-Step "STEP 3: AI-Powered Answer Evaluation" "STEP"

try {
    $evalBody = @{
        examId = $ExamId
        questionId = $QuestionId
        studentAnswerText = $extractedText
        extractedText = $extractedText
        blobPath = $blobPath
    } | ConvertTo-Json
    
    $evalStartTime = Get-Date
    Write-Step "Sending to AI evaluation engine..." "PROGRESS"
    Write-Step "Comparing with ideal answer and generating feedback..." "PROGRESS"
    
    $evalResponse = Invoke-RestMethod `
        -Uri "$BaseUrl/answers/evaluate?code=$FunctionKey" `
        -Method POST `
        -Body $evalBody `
        -ContentType "application/json" `
        -TimeoutSec 120
    
    $evalDuration = (Get-Date) - $evalStartTime
    
    Write-Step "Evaluation completed in $([math]::Round($evalDuration.TotalSeconds, 2)) seconds" "SUCCESS"
    
} catch {
    Write-Step "Evaluation failed: $($_.Exception.Message)" "ERROR"
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $errorBody = $reader.ReadToEnd()
        Write-Host "Error details: $errorBody" -ForegroundColor Red
    }
    exit 1
}

# ═══════════════════════════════════════════════════════════
# STEP 4: Display Results with Step-wise Breakdown
# ═══════════════════════════════════════════════════════════
Write-Step "STEP 4: Final Results and Step-wise Marks" "STEP"

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                   EVALUATION RESULTS                   ║" -ForegroundColor Cyan
Write-Host "╠═══════════════════════════════════════════════════════════╣" -ForegroundColor Cyan
Write-Host "║  Evaluation ID: $($evalResponse.evaluationId)" -ForegroundColor White
Write-Host "║  Exam ID:       $($evalResponse.examId)" -ForegroundColor White
Write-Host "║  Question ID:   $($evalResponse.questionId)" -ForegroundColor White
Write-Host "╠═══════════════════════════════════════════════════════════╣" -ForegroundColor Cyan
Write-Host "║                      SCORE SUMMARY                     ║" -ForegroundColor Cyan
Write-Host "╠═══════════════════════════════════════════════════════════╣" -ForegroundColor Cyan
Write-Host "║  Score:         $($evalResponse.score)/$($evalResponse.maxMarks) marks" -ForegroundColor Green
Write-Host "║  Percentage:    $($evalResponse.percentage)%" -ForegroundColor $(if($evalResponse.percentage -ge 60){"Green"}else{"Yellow"})
Write-Host "║  Status:        $(if($evalResponse.isComplete){'Complete'}else{'Incomplete'})" -ForegroundColor $(if($evalResponse.isComplete){"Green"}else{"Yellow"})
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Display Step-wise Breakdown
if ($evalResponse.stepWiseBreakdown -and $evalResponse.stepWiseBreakdown.Count -gt 0) {
    Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
    Write-Host "║              STEP-WISE MARKS BREAKDOWN                ║" -ForegroundColor Yellow
    Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
    Write-Host ""
    
    foreach ($step in $evalResponse.stepWiseBreakdown) {
        $statusIcon = switch ($step.status) {
            "Complete" { "[OK]" }
            "Partial" { "[~]" }
            "Missing" { "[X]" }
            default { "•" }
        }
        
        $color = switch ($step.status) {
            "Complete" { "Green" }
            "Partial" { "Yellow" }
            "Missing" { "Red" }
            default { "Gray" }
        }
        
        Write-Host "$statusIcon Step $($step.stepNumber): " -NoNewline -ForegroundColor $color
        Write-Host "$($step.stepDescription)" -ForegroundColor White
        Write-Host "   Marks: $($step.marksAwarded)/$($step.maxMarks)" -ForegroundColor $color
        Write-Host "   $($step.feedback)" -ForegroundColor Gray
        Write-Host ""
    }
}

# Display Feedback
Write-Host "╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║                    DETAILED FEEDBACK                   ║" -ForegroundColor Magenta
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""
Write-Host "Overall Feedback:" -ForegroundColor Cyan
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

# Display Keywords Analysis
if ($evalResponse.keywordsMatched -and $evalResponse.keywordsMatched.Count -gt 0) {
    Write-Host "Concepts Covered: " -NoNewline -ForegroundColor Green
    Write-Host ($evalResponse.keywordsMatched -join ", ") -ForegroundColor White
}

if ($evalResponse.missingKeywords -and $evalResponse.missingKeywords.Count -gt 0) {
    Write-Host "Missing Concepts: " -NoNewline -ForegroundColor Red
    Write-Host ($evalResponse.missingKeywords -join ", ") -ForegroundColor White
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor White
Write-Host "           FULL EVALUATION FLOW COMPLETED               " -ForegroundColor Green
Write-Host "═══════════════════════════════════════════════════════════" -ForegroundColor White
Write-Host ""

# Save results to file
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultsFile = "C:\SmartStudyFunc\evaluation-results-$timestamp.json"
$evalResponse | ConvertTo-Json -Depth 10 | Out-File $resultsFile
Write-Step "Results saved to: $resultsFile" "SUCCESS"
