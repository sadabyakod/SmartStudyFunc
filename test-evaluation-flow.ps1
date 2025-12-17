# Test script for SmartStudy answer sheet evaluation flow
# This tests: Upload -> OCR Processing -> AI Evaluation -> Status Updates -> DB Updates

$baseUrl = "http://localhost:7071/api"

Write-Host "=== SmartStudy Evaluation Flow Test ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Health Check
Write-Host "Step 1: Health Check" -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "$baseUrl/health" -Method GET
    Write-Host "  Status: $($health.status)" -ForegroundColor Green
    Write-Host "  Database: $($health.database)" -ForegroundColor Green
    Write-Host "  SQL Configured: $($health.sql_configured)" -ForegroundColor Green
    Write-Host "  OpenAI Configured: $($health.openai_configured)" -ForegroundColor Green
} catch {
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 2: Create a test image file (simple PNG with text)
Write-Host "Step 2: Creating test answer sheet image..." -ForegroundColor Yellow
$testImagePath = "c:\SmartStudyFunc\SmartStudyFunc\test-answer-sheet.txt"

# Create a simple text file that simulates a student answer
$answerContent = @"
Question 1: What is photosynthesis?
Answer: Photosynthesis is the process by which plants convert light energy, usually from the sun, into chemical energy in the form of glucose. The process takes place in the chloroplasts and uses carbon dioxide from the air and water from the soil. The chemical equation is: 6CO2 + 6H2O + light energy -> C6H12O6 + 6O2

Question 2: Explain Newton's three laws of motion.
Answer:
First Law (Inertia): An object at rest stays at rest and an object in motion stays in motion with the same speed and direction unless acted upon by an external force.

Second Law: Force equals mass times acceleration (F = ma). The acceleration of an object depends on the mass of the object and the force applied.

Third Law: For every action, there is an equal and opposite reaction.

Question 3: What is the water cycle?
Answer: The water cycle describes how water evaporates from the surface of the earth, rises into the atmosphere, cools and condenses into clouds, and falls back to the surface as precipitation. This continuous movement includes evaporation, condensation, precipitation, and collection.
"@
$answerContent | Out-File -FilePath $testImagePath -Encoding UTF8
Write-Host "  Created test file at: $testImagePath" -ForegroundColor Green
Write-Host ""

# Step 3: Upload the answer sheet
Write-Host "Step 3: Uploading answer sheet..." -ForegroundColor Yellow

# For file upload, we need to use a different approach
$examId = "EXAM-$(Get-Date -Format 'yyyyMMdd')-001"
$studentId = "STUDENT-TEST-001"

# Create boundary for multipart form
$boundary = [System.Guid]::NewGuid().ToString()

# Read file bytes
$fileBytes = [System.IO.File]::ReadAllBytes($testImagePath)
$fileName = "test-answer-sheet.txt"

# Build multipart form data
$LF = "`r`n"
$bodyLines = (
    "--$boundary",
    "Content-Disposition: form-data; name=`"examId`"",
    "",
    $examId,
    "--$boundary",
    "Content-Disposition: form-data; name=`"studentId`"",
    "",
    $studentId,
    "--$boundary",
    "Content-Disposition: form-data; name=`"file`"; filename=`"test-answer.png`"",
    "Content-Type: image/png",
    ""
) -join $LF

$bodyEnd = "$LF--$boundary--$LF"

# Convert to bytes
$enc = [System.Text.Encoding]::UTF8
$bodyStart = $enc.GetBytes($bodyLines + $LF)
$bodyEndBytes = $enc.GetBytes($bodyEnd)

# Combine all parts
$body = [byte[]]::new($bodyStart.Length + $fileBytes.Length + $bodyEndBytes.Length)
[System.Buffer]::BlockCopy($bodyStart, 0, $body, 0, $bodyStart.Length)
[System.Buffer]::BlockCopy($fileBytes, 0, $body, $bodyStart.Length, $fileBytes.Length)
[System.Buffer]::BlockCopy($bodyEndBytes, 0, $body, $bodyStart.Length + $fileBytes.Length, $bodyEndBytes.Length)

try {
    $uploadResponse = Invoke-RestMethod -Uri "$baseUrl/answers/upload" -Method POST -ContentType "multipart/form-data; boundary=$boundary" -Body $body
    Write-Host "  Upload Success!" -ForegroundColor Green
    Write-Host "  Submission ID: $($uploadResponse.submissionId)" -ForegroundColor Cyan
    Write-Host "  Initial Status: $($uploadResponse.status)" -ForegroundColor Cyan
    Write-Host "  Blob Path: $($uploadResponse.blobPath)" -ForegroundColor Gray
    $submissionId = $uploadResponse.submissionId
} catch {
    Write-Host "  Upload Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Step 4: Poll for status updates
Write-Host "Step 4: Polling for status updates..." -ForegroundColor Yellow

$statusMap = @{
    "0" = "Uploaded"
    "1" = "OcrProcessing"
    "2" = "Evaluating"
    "3" = "Completed"
    "4" = "Failed"
    "Uploaded" = "Uploaded"
    "OcrProcessing" = "OcrProcessing"
    "Evaluating" = "Evaluating"
    "Completed" = "Completed"
    "Failed" = "Failed"
}

$maxPolls = 30
$pollInterval = 5
$previousStatus = ""

for ($i = 1; $i -le $maxPolls; $i++) {
    try {
        $statusResponse = Invoke-RestMethod -Uri "$baseUrl/submissions/$submissionId" -Method GET
        $currentStatus = $statusResponse.status
        
        if ($currentStatus -ne $previousStatus) {
            $timestamp = Get-Date -Format "HH:mm:ss"
            
            switch ($currentStatus) {
                {$_ -eq 0 -or $_ -eq "Uploaded"} {
                    Write-Host "  [$timestamp] Status: UPLOADED - Waiting for OCR..." -ForegroundColor Gray
                }
                {$_ -eq 1 -or $_ -eq "OcrProcessing"} {
                    Write-Host "  [$timestamp] Status: OCR PROCESSING - Extracting text from answer sheet..." -ForegroundColor Yellow
                }
                {$_ -eq 2 -or $_ -eq "Evaluating"} {
                    Write-Host "  [$timestamp] Status: EVALUATING - AI is scoring your answers..." -ForegroundColor Blue
                }
                {$_ -eq 3 -or $_ -eq "Completed"} {
                    Write-Host "  [$timestamp] Status: COMPLETED!" -ForegroundColor Green
                    Write-Host ""
                    Write-Host "=== EVALUATION RESULTS ===" -ForegroundColor Cyan
                    Write-Host "  Total Score: $($statusResponse.totalScore)/$($statusResponse.maxScore)" -ForegroundColor Green
                    Write-Host "  Percentage: $($statusResponse.percentage)%" -ForegroundColor Green
                    Write-Host "  Questions Evaluated: $($statusResponse.questionCount)" -ForegroundColor Cyan
                    Write-Host ""
                    
                    if ($statusResponse.evaluations) {
                        Write-Host "=== QUESTION-WISE FEEDBACK ===" -ForegroundColor Cyan
                        foreach ($eval in $statusResponse.evaluations) {
                            Write-Host "  Q$($eval.questionNumber): $($eval.score)/$($eval.maxScore)" -ForegroundColor Yellow
                            if ($eval.feedback) {
                                Write-Host "    Feedback: $($eval.feedback)" -ForegroundColor Gray
                            }
                        }
                    }
                    break
                }
                {$_ -eq 4 -or $_ -eq "Failed"} {
                    Write-Host "  [$timestamp] Status: FAILED" -ForegroundColor Red
                    if ($statusResponse.errorMessage) {
                        Write-Host "  Error: $($statusResponse.errorMessage)" -ForegroundColor Red
                    }
                    break
                }
            }
            
            $previousStatus = $currentStatus
        }
        
        if ($currentStatus -eq 3 -or $currentStatus -eq "Completed" -or $currentStatus -eq 4 -or $currentStatus -eq "Failed") {
            break
        }
        
        # Show waiting dots
        Write-Host "  Poll $i/$maxPolls - Waiting ${pollInterval}s..." -ForegroundColor DarkGray
        Start-Sleep -Seconds $pollInterval
        
    } catch {
        Write-Host "  Error polling status: $($_.Exception.Message)" -ForegroundColor Red
        Start-Sleep -Seconds $pollInterval
    }
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan

# Cleanup
if (Test-Path $testImagePath) {
    Remove-Item $testImagePath -Force
    Write-Host "Cleaned up test file" -ForegroundColor Gray
}
