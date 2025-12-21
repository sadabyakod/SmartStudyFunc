# Simple Test Script for Answer Sheet Evaluation
$ImagePath = "C:\SmartStudyFunc\test-answer-sheet.jpg"
$FunctionKey = "YOUR_FUNCTION_KEY_HERE"
$BaseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$ExamId = "TEST_EXAM_001"
$QuestionId = "TEST_Q_001"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STEP 1: Checking Image File" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

if (!(Test-Path $ImagePath)) {
    Write-Host "ERROR: Image file not found at $ImagePath" -ForegroundColor Red
    exit 1
}

$fileInfo = Get-Item $ImagePath
Write-Host "Image found: $($fileInfo.Length) bytes" -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STEP 2: Uploading and OCR Extraction" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

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
    [System.Text.Encoding]::GetEncoding('iso-8859-1').GetString($fileBin),
    "--$boundary--$LF"
)

$bodyString = $bodyLines -join $LF

try {
    $uploadResponse = Invoke-RestMethod `
        -Uri "$BaseUrl/api/answers/upload" `
        -Method Post `
        -Headers @{
            "x-functions-key" = $FunctionKey
            "Content-Type" = "multipart/form-data; boundary=$boundary"
        } `
        -Body ([System.Text.Encoding]::GetEncoding('iso-8859-1').GetBytes($bodyString))
    
    Write-Host "Upload successful!" -ForegroundColor Green
    Write-Host "Extracted Text Length: $($uploadResponse.extractedText.Length) characters" -ForegroundColor Cyan
    Write-Host "Blob Path: $($uploadResponse.blobPath)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "--- EXTRACTED TEXT ---" -ForegroundColor Gray
    Write-Host $uploadResponse.extractedText -ForegroundColor White
    Write-Host "--- END EXTRACTED TEXT ---" -ForegroundColor Gray
    
    $extractedText = $uploadResponse.extractedText
    $blobPath = $uploadResponse.blobPath
    
} catch {
    Write-Host "Upload failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "STEP 3: AI Evaluation" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

$evalBody = @{
    examId = $ExamId
    questionId = $QuestionId
    studentAnswerText = $extractedText
    extractedText = $extractedText
    blobPath = $blobPath
    expectedAnswer = "This is a calculus problem involving differentiation. The expected answer should include: 1) Using the chain rule for differentiation, 2) Applying product rule where needed, 3) Simplifying the final expression, 4) Showing all intermediate steps clearly."
    maxMarks = 20
} | ConvertTo-Json

try {
    $evalResponse = Invoke-RestMethod `
        -Uri "$BaseUrl/api/answers/evaluate" `
        -Method Post `
        -Headers @{
            "x-functions-key" = $FunctionKey
            "Content-Type" = "application/json"
        } `
        -Body $evalBody
    
    Write-Host "Evaluation complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "EVALUATION RESULTS" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Evaluation ID: $($evalResponse.evaluationId)" -ForegroundColor White
    Write-Host "Score: $($evalResponse.score) / $($evalResponse.maxMarks) ($($evalResponse.percentage)%)" -ForegroundColor $(if($evalResponse.percentage -ge 70){"Green"}elseif($evalResponse.percentage -ge 40){"Yellow"}else{"Red"})
    Write-Host "Status: $(if($evalResponse.isComplete){'Complete'}else{'Incomplete'})" -ForegroundColor $(if($evalResponse.isComplete){"Green"}else{"Yellow"})
    Write-Host ""
    
    if ($evalResponse.stepWiseBreakdown -and $evalResponse.stepWiseBreakdown.Count -gt 0) {
        Write-Host "--- STEP-WISE BREAKDOWN ---" -ForegroundColor Yellow
        foreach ($step in $evalResponse.stepWiseBreakdown) {
            $icon = if($step.marksAwarded -eq $step.maxMarks){"[OK]"}elseif($step.marksAwarded -gt 0){"[~]"}else{"[X]"}
            Write-Host "$icon Step $($step.stepNumber): $($step.stepDescription)" -ForegroundColor White
            Write-Host "   Marks: $($step.marksAwarded)/$($step.maxMarks) - $($step.feedback)" -ForegroundColor Gray
        }
        Write-Host ""
    }
    
    Write-Host "--- FEEDBACK ---" -ForegroundColor Magenta
    Write-Host $evalResponse.feedback -ForegroundColor White
    Write-Host ""
    
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
    
    # Save results
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $resultsFile = "C:\SmartStudyFunc\evaluation-results-$timestamp.json"
    $evalResponse | ConvertTo-Json -Depth 10 | Out-File $resultsFile
    Write-Host "Results saved to: $resultsFile" -ForegroundColor Cyan
    
} catch {
    Write-Host "Evaluation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
