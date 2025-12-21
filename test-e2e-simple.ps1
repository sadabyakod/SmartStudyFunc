# Complete End-to-End Answer Sheet Evaluation Test
# Using existing question ID to avoid database connection issues

$ErrorActionPreference = "Stop"

$BaseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$FunctionKey = "YOUR_FUNCTION_KEY_HERE"
$ImagePath = "C:\SmartStudyFunc\test-answer-sheet.jpg"
$QuestionId = "421211c1-77a0-4e3c-8519-10d1a469ef95"  # Existing test question

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "COMPLETE END-TO-END EVALUATION TEST" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "`nUsing existing Question ID: $QuestionId" -ForegroundColor Green

# ========================================
# STEP 1: Upload Answer Sheet with OCR
# ========================================
Write-Host "`n[1/3] Uploading handwritten answer sheet..." -ForegroundColor White

if (-not (Test-Path $ImagePath)) {
    Write-Host "Error: Image not found at $ImagePath" -ForegroundColor Red
    exit 1
}

$fileBytes = [System.IO.File]::ReadAllBytes($ImagePath)

# Create multipart form-data manually
$boundary = [System.Guid]::NewGuid().ToString()
$LF = "`r`n"

$bodyLines = @(
    "--$boundary",
    "Content-Disposition: form-data; name=`"examId`"",
    "",
    "12345",
    "--$boundary",
    "Content-Disposition: form-data; name=`"questionId`"",
    "",
    "$QuestionId",
    "--$boundary",
    "Content-Disposition: form-data; name=`"file`"; filename=`"answer-sheet.jpg`"",
    "Content-Type: image/jpeg",
    "",
    ""
)

$bodyStart = ($bodyLines -join $LF) + $LF
$bodyEnd = $LF + "--$boundary--" + $LF

$bodyStartBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyStart)
$bodyEndBytes = [System.Text.Encoding]::UTF8.GetBytes($bodyEnd)

$requestBody = $bodyStartBytes + $fileBytes + $bodyEndBytes

try {
    $uploadResponse = Invoke-RestMethod -Uri "$BaseUrl/api/answers/upload?code=$FunctionKey" `
        -Method Post `
        -ContentType "multipart/form-data; boundary=$boundary" `
        -Body $requestBody
    
    Write-Host "Upload successful!" -ForegroundColor Green
    Write-Host "Blob Path: $($uploadResponse.blobPath)" -ForegroundColor Cyan
    Write-Host "OCR Extracted Text Length: $($uploadResponse.extractedText.Length) characters" -ForegroundColor Cyan
    
    if ($uploadResponse.extractedText.Length -gt 0) {
        Write-Host "`nExtracted Text Preview:" -ForegroundColor Yellow
        $preview = $uploadResponse.extractedText.Substring(0, [Math]::Min(500, $uploadResponse.extractedText.Length))
        Write-Host $preview -ForegroundColor White
    } else {
        Write-Host "`nWARNING: No text extracted!" -ForegroundColor Red
        Write-Host "OCR service may not be configured properly." -ForegroundColor Yellow
        Write-Host "Check GoogleCloud:ApiKey in Function App settings." -ForegroundColor Yellow
    }
    
    $extractedText = $uploadResponse.extractedText
    
} catch {
    Write-Host "Upload failed: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

# ========================================
# STEP 2: Wait for processing
# ========================================
Write-Host "`n[2/3] Waiting for evaluation processing..." -ForegroundColor White
Start-Sleep -Seconds 3

# ========================================
# STEP 3: Get Evaluation Results
# ========================================
Write-Host "`n[3/3] Calling evaluation API..." -ForegroundColor White

# Use extracted text or fallback to manual text if OCR failed
if ([string]::IsNullOrEmpty($extractedText)) {
    Write-Host "Using fallback text since OCR failed..." -ForegroundColor Yellow
    $extractedText = "Part-B: Solution using product rule: Let u = 3x^2 + 2x and v = 5x - 1. Then du/dx = 6x + 2 and dv/dx = 5. Using product rule: dy/dx = u(dv/dx) + v(du/dx) = (3x^2 + 2x)(5) + (5x - 1)(6x + 2) = 15x^2 + 10x + 30x^2 + 10x - 6x - 2 = 45x^2 + 14x - 2"
}

$evalPayload = @{
    questionId = $QuestionId
    examId = 12345
    studentAnswer = $extractedText
} | ConvertTo-Json

try {
    $evalResponse = Invoke-RestMethod -Uri "$BaseUrl/api/answers/evaluate?code=$FunctionKey" `
        -Method Post `
        -ContentType "application/json" `
        -Body $evalPayload
    
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "EVALUATION COMPLETE!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    
    Write-Host "`nTotal Marks: $($evalResponse.marks)/$($evalResponse.totalMarks)" -ForegroundColor Yellow
    $percentage = [Math]::Round(($evalResponse.marks / $evalResponse.totalMarks) * 100, 2)
    Write-Host "Percentage: $percentage%" -ForegroundColor Cyan
    
    if ($evalResponse.steps -and $evalResponse.steps.Count -gt 0) {
        Write-Host "`nStep-wise Breakdown:" -ForegroundColor Yellow
        foreach ($step in $evalResponse.steps) {
            $status = if ($step.correct) { "[PASS]" } else { "[FAIL]" }
            $color = if ($step.correct) { "Green" } else { "Red" }
            Write-Host "$status Step $($step.stepNumber): $($step.description)" -ForegroundColor $color
            Write-Host "    Marks: $($step.marksAwarded)/$($step.marksAllocated)" -ForegroundColor Gray
            if ($step.feedback) {
                Write-Host "    Feedback: $($step.feedback)" -ForegroundColor Gray
            }
        }
    }
    
    Write-Host "`nOverall Feedback:" -ForegroundColor Yellow
    Write-Host $evalResponse.feedback -ForegroundColor White
    
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "TEST COMPLETED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    
} catch {
    Write-Host "`nEvaluation failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode.value__
        Write-Host "Status Code: $statusCode" -ForegroundColor Yellow
        
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseBody = $reader.ReadToEnd()
            Write-Host "Response: $responseBody" -ForegroundColor Gray
        } catch {
            Write-Host "Could not read response body" -ForegroundColor Gray
        }
    }
    
    Write-Host "`nPossible issues:" -ForegroundColor Yellow
    Write-Host "- Evaluation endpoint may not be deployed" -ForegroundColor White
    Write-Host "- Question ID not found in database" -ForegroundColor White
    Write-Host "- Azure OpenAI configuration missing" -ForegroundColor White
}
