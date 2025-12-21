# Complete End-to-End Answer Sheet Evaluation Test
# This script performs the full flow: Question creation → Upload → OCR → Evaluation

$ErrorActionPreference = "Stop"

$BaseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$FunctionKey = "YOUR_FUNCTION_KEY_HERE"
$ImagePath = "C:\SmartStudyFunc\test-answer-sheet.jpg"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "COMPLETE END-TO-END EVALUATION TEST" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

# ========================================
# STEP 1: Create Test Question
# ========================================
Write-Host "`n[1/4] Creating test question in database..." -ForegroundColor White

# Generate a new GUID for the question
$QuestionId = [Guid]::NewGuid().ToString()
Write-Host "Generated Question ID: $QuestionId" -ForegroundColor Yellow

$SqlQuery = @"
INSERT INTO ExamQuestions (
    Id,
    ExamId,
    Text,
    Marks,
    ModelAnswer,
    Keywords,
    CreatedAt
)
VALUES (
    '$QuestionId',
    12345,
    'Part-B: Differentiate the following function with respect to x using product rule: y = (3x² + 2x)(5x - 1)',
    20,
    'Step 1: Identify u and v
Let u = 3x² + 2x and v = 5x - 1

Step 2: Find derivatives
du/dx = 6x + 2
dv/dx = 5

Step 3: Apply product rule
dy/dx = u(dv/dx) + v(du/dx)
dy/dx = (3x² + 2x)(5) + (5x - 1)(6x + 2)

Step 4: Expand first term
= 15x² + 10x + (5x - 1)(6x + 2)

Step 5: Expand second term
= 15x² + 10x + 30x² + 10x - 6x - 2

Step 6: Combine like terms
= 45x² + 14x - 2

Final Answer: dy/dx = 45x² + 14x - 2',
    'product rule, derivative, differentiate, du/dx, dv/dx, expand, combine like terms, 45x², 14x, -2',
    GETUTCDATE()
);

SELECT '$QuestionId' AS QuestionId;
"@

try {
    # Using .NET SqlClient since Invoke-Sqlcmd may not be available
    $connectionString = "Server=tcp:smartstudysqlsrv.database.windows.net,1433;Initial Catalog=smartstudydb;User ID=sqladmin;Password=Admin@12345;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    
    $connection = New-Object System.Data.SqlClient.SqlConnection
    $connection.ConnectionString = $connectionString
    $connection.Open()
    
    $command = $connection.CreateCommand()
    $command.CommandText = $SqlQuery
    $command.ExecuteNonQuery() | Out-Null
    
    $connection.Close()
    
    Write-Host "Question created successfully!" -ForegroundColor Green
    Write-Host "Question ID: $QuestionId" -ForegroundColor Cyan
} catch {
    Write-Host "Failed to create question: $_" -ForegroundColor Red
    exit 1
}

# ========================================
# STEP 2: Upload Answer Sheet with OCR
# ========================================
Write-Host "`n[2/4] Uploading handwritten answer sheet..." -ForegroundColor White

if (-not (Test-Path $ImagePath)) {
    Write-Host "Error: Image not found at $ImagePath" -ForegroundColor Red
    exit 1
}

$fileBytes = [System.IO.File]::ReadAllBytes($ImagePath)
$fileBase64 = [System.Convert]::ToBase64String($fileBytes)

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
        Write-Host ($uploadResponse.extractedText.Substring(0, [Math]::Min(500, $uploadResponse.extractedText.Length)))
    } else {
        Write-Host "Warning: No text extracted. OCR may not be configured." -ForegroundColor Yellow
    }
} catch {
    Write-Host "Upload failed: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

# ========================================
# STEP 3: Wait for processing
# ========================================
Write-Host "`n[3/4] Waiting for evaluation processing..." -ForegroundColor White
Start-Sleep -Seconds 5

# ========================================
# STEP 4: Get Evaluation Results
# ========================================
Write-Host "`n[4/4] Calling evaluation API..." -ForegroundColor White

$evalPayload = @{
    questionId = $QuestionId
    examId = 12345
    studentAnswer = $uploadResponse.extractedText
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
    Write-Host "Percentage: $([Math]::Round(($evalResponse.marks / $evalResponse.totalMarks) * 100, 2))%" -ForegroundColor Cyan
    
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
    
    Write-Host "`nOverall Feedback:" -ForegroundColor Yellow
    Write-Host $evalResponse.feedback -ForegroundColor White
    
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "TEST COMPLETED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    
} catch {
    Write-Host "Evaluation failed: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response: $responseBody" -ForegroundColor Gray
    }
}
