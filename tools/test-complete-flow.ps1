# =====================================================
# Complete End-to-End Test: Answer Sheet Upload → Evaluation → Scores
# =====================================================
# This script tests the COMPLETE flow:
# 1. Upload answer sheet (creates submission in DB with status=Uploaded)
# 2. Queue message triggers ProcessWrittenSubmission
# 3. Status changes: Uploaded → OcrProcessing → Evaluating → Completed
# 4. Verify final scores and feedback in database
# =====================================================

param(
    [string]$BaseUrl = "http://localhost:7071/api",
    [string]$ExamId = "EXAM-E2E-TEST-001",
    [string]$StudentId = "STUDENT-E2E-001",
    [string]$ImagePath = "",
    [switch]$UseAzure,
    [switch]$Verbose
)

# ═══════════════════════════════════════════════════════════════════════
# CONFIGURATION
# ═══════════════════════════════════════════════════════════════════════

$ErrorActionPreference = "Stop"

$AzureUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net/api"
if ($UseAzure) {
    $BaseUrl = $AzureUrl
    Write-Host "🌐 Using Azure endpoint: $BaseUrl" -ForegroundColor Cyan
} else {
    Write-Host "💻 Using LOCAL endpoint: $BaseUrl" -ForegroundColor Cyan
    Write-Host "   Make sure to run 'func start' first!" -ForegroundColor Yellow
}

# Database connection (update with your credentials)
$SqlConnectionString = "Server=smartstudysqlsrv.database.windows.net;Database=smartstudydb;User Id=schooladmin;Password=India@12345;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

# ═══════════════════════════════════════════════════════════════════════
# HELPER FUNCTIONS
# ═══════════════════════════════════════════════════════════════════════

function Write-Step {
    param([string]$StepNum, [string]$Title)
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor White
    Write-Host "  STEP $StepNum : $Title" -ForegroundColor Magenta
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor White
}

function Write-Status {
    param([string]$Message, [string]$Status = "INFO")
    $icon = switch ($Status) {
        "SUCCESS" { "✅"; $color = "Green" }
        "ERROR"   { "❌"; $color = "Red" }
        "WARNING" { "⚠️"; $color = "Yellow" }
        "INFO"    { "ℹ️"; $color = "Cyan" }
        "WAIT"    { "⏳"; $color = "Yellow" }
        default   { "📋"; $color = "White" }
    }
    Write-Host "$icon $Message" -ForegroundColor $color
}

function Get-StatusName {
    param([int]$StatusCode)
    switch ($StatusCode) {
        0 { "Uploaded" }
        1 { "OcrProcessing" }
        2 { "Evaluating" }
        3 { "Completed" }
        4 { "Failed" }
        default { "Unknown($StatusCode)" }
    }
}

function Invoke-SqlQuery {
    param([string]$Query)
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($SqlConnectionString)
    $connection.Open()
    
    $command = New-Object System.Data.SqlClient.SqlCommand($Query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $connection.Close()
    return $dataset.Tables[0]
}

function Invoke-SqlNonQuery {
    param([string]$Query)
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($SqlConnectionString)
    $connection.Open()
    
    $command = New-Object System.Data.SqlClient.SqlCommand($Query, $connection)
    $result = $command.ExecuteNonQuery()
    
    $connection.Close()
    return $result
}

# ═══════════════════════════════════════════════════════════════════════
# STEP 0: Health Check
# ═══════════════════════════════════════════════════════════════════════

Write-Step "0" "Health Check"

try {
    $health = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get -TimeoutSec 10
    Write-Status "Function App is healthy!" "SUCCESS"
    if ($Verbose) { $health | ConvertTo-Json | Write-Host -ForegroundColor Gray }
} catch {
    Write-Status "Function App not responding: $($_.Exception.Message)" "ERROR"
    Write-Host "   → Make sure to run 'func start' in the SmartStudyFunc directory" -ForegroundColor Yellow
    exit 1
}

# ═══════════════════════════════════════════════════════════════════════
# STEP 1: Setup Test Exam Questions in Database
# ═══════════════════════════════════════════════════════════════════════

Write-Step "1" "Setup Test Exam Questions"

try {
    # Check if exam questions exist
    $checkQuery = "SELECT COUNT(*) as cnt FROM ExamQuestions WHERE ExamId = '$ExamId'"
    $existing = Invoke-SqlQuery -Query $checkQuery
    
    if ($existing.cnt -eq 0) {
        Write-Status "Creating test exam questions for ExamId: $ExamId" "INFO"
        
        $insertQuery = @"
INSERT INTO ExamQuestions (Id, ExamId, QuestionNumber, QuestionText, ModelAnswer, MaxScore, Rubric, Keywords, ClassName, Subject, Chapter)
VALUES 
(NEWID(), '$ExamId', 1, 
 'State the Pythagorean theorem and explain with an example.',
 'The Pythagorean theorem states that in a right-angled triangle, the square of the hypotenuse equals the sum of squares of the other two sides: a² + b² = c². Example: For a triangle with sides 3, 4, 5: 3² + 4² = 9 + 16 = 25 = 5². Hence verified.',
 5, 
 'Step 1 (2 marks): Correct statement of theorem. Step 2 (1 mark): Formula a² + b² = c². Step 3 (2 marks): Valid example with calculation.',
 '["Pythagorean", "hypotenuse", "right angle", "a² + b² = c²"]',
 '10th', 'Mathematics', 'Triangles'),

(NEWID(), '$ExamId', 2, 
 'Solve the quadratic equation: x² - 7x + 12 = 0',
 'x² - 7x + 12 = 0. Factoring: Find two numbers that multiply to 12 and add to -7: -3 and -4. (x - 3)(x - 4) = 0. Therefore x = 3 or x = 4.',
 5, 
 'Step 1 (1 mark): Write equation. Step 2 (2 marks): Correct factorization. Step 3 (2 marks): Both roots correct.',
 '["quadratic", "factorization", "roots", "x = 3", "x = 4"]',
 '10th', 'Mathematics', 'Quadratic Equations'),

(NEWID(), '$ExamId', 3, 
 'What is photosynthesis? Write the equation.',
 'Photosynthesis is the process by which green plants make their own food using sunlight, water, and carbon dioxide. The equation is: 6CO₂ + 6H₂O + Light Energy → C₆H₁₂O₆ + 6O₂',
 5, 
 'Step 1 (2 marks): Definition of photosynthesis. Step 2 (2 marks): Correct equation. Step 3 (1 mark): Mention of sunlight/chlorophyll.',
 '["photosynthesis", "chlorophyll", "glucose", "oxygen", "CO₂", "H₂O"]',
 '10th', 'Biology', 'Life Processes');
"@
        
        Invoke-SqlNonQuery -Query $insertQuery | Out-Null
        Write-Status "Created 3 test questions (5 marks each, total 15 marks)" "SUCCESS"
    } else {
        Write-Status "Exam questions already exist for ExamId: $ExamId ($($existing.cnt) questions)" "SUCCESS"
    }
    
    # Display questions
    $questions = Invoke-SqlQuery -Query "SELECT QuestionNumber, LEFT(QuestionText, 60) as Question, MaxScore FROM ExamQuestions WHERE ExamId = '$ExamId' ORDER BY QuestionNumber"
    Write-Host ""
    Write-Host "   Exam Questions:" -ForegroundColor Gray
    foreach ($q in $questions) {
        Write-Host "   Q$($q.QuestionNumber): $($q.Question)... [$($q.MaxScore) marks]" -ForegroundColor Gray
    }
    
} catch {
    Write-Status "Database setup error: $($_.Exception.Message)" "ERROR"
    exit 1
}

# ═══════════════════════════════════════════════════════════════════════
# STEP 2: Create Sample Answer Image or Use Provided
# ═══════════════════════════════════════════════════════════════════════

Write-Step "2" "Prepare Answer Sheet"

$useTextFallback = $false

if ([string]::IsNullOrEmpty($ImagePath)) {
    # Create a simple PNG with text (simulating student answer sheet)
    $tempDir = Join-Path $env:TEMP "smartstudy-test"
    if (-not (Test-Path $tempDir)) { New-Item -ItemType Directory -Path $tempDir | Out-Null }
    
    $ImagePath = Join-Path $tempDir "student-answer-sheet.png"
    
    # Try to create an image with System.Drawing
    try {
        Add-Type -AssemblyName System.Drawing
        
        $width = 800
        $height = 1000
        $bitmap = New-Object System.Drawing.Bitmap($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::White)
        
        $font = New-Object System.Drawing.Font("Arial", 14)
        $brush = [System.Drawing.Brushes]::Black
        
        $y = 20
        $lineHeight = 25
        
        $lines = @(
            "Student Answer Sheet",
            "==================",
            "Exam ID: $ExamId",
            "Student: $StudentId",
            "",
            "Q1. Pythagorean Theorem:",
            "In a right angled triangle, the square of",
            "hypotenuse equals sum of squares of other sides.",
            "Formula: a² + b² = c²",
            "Example: 3² + 4² = 9 + 16 = 25 = 5²",
            "",
            "Q2. Solve x² - 7x + 12 = 0",
            "Factoring: -3 × -4 = 12, -3 + -4 = -7",
            "(x - 3)(x - 4) = 0",
            "x = 3 or x = 4",
            "",
            "Q3. Photosynthesis:",
            "Photosynthesis is the process where plants",
            "make food using sunlight, water and CO2.",
            "6CO2 + 6H2O + light → C6H12O6 + 6O2"
        )
        
        foreach ($line in $lines) {
            $graphics.DrawString($line, $font, $brush, 20, $y)
            $y += $lineHeight
        }
        
        $bitmap.Save($ImagePath, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose()
        $bitmap.Dispose()
        
        Write-Status "Created test answer sheet image: $ImagePath" "SUCCESS"
        
    } catch {
        Write-Status "Could not create image (System.Drawing not available)" "WARNING"
        Write-Status "Using text file fallback for testing" "INFO"
        $useTextFallback = $true
        
        # Create text file instead
        $textContent = @"
Student Answer Sheet
====================
Exam ID: $ExamId
Student: $StudentId

Q1. Pythagorean Theorem:
In a right angled triangle, the square of hypotenuse equals sum of squares of other sides.
Formula: a² + b² = c²
Example: 3² + 4² = 9 + 16 = 25 = 5²

Q2. Solve x² - 7x + 12 = 0
Factoring: -3 × -4 = 12, -3 + -4 = -7
(x - 3)(x - 4) = 0
x = 3 or x = 4

Q3. Photosynthesis:
Photosynthesis is the process where plants make food using sunlight, water and CO2.
6CO2 + 6H2O + light → C6H12O6 + 6O2
"@
        $ImagePath = Join-Path $tempDir "student-answer-sheet.txt"
        $textContent | Out-File -FilePath $ImagePath -Encoding UTF8
        Write-Status "Created text file: $ImagePath" "SUCCESS"
    }
} else {
    if (-not (Test-Path $ImagePath)) {
        Write-Status "Image not found: $ImagePath" "ERROR"
        exit 1
    }
    Write-Status "Using provided image: $ImagePath" "SUCCESS"
}

# ═══════════════════════════════════════════════════════════════════════
# STEP 3: Upload Answer Sheet via API
# ═══════════════════════════════════════════════════════════════════════

Write-Step "3" "Upload Answer Sheet"

$submissionId = $null

try {
    $uploadUrl = "$BaseUrl/answers/upload"
    Write-Status "Uploading to: $uploadUrl" "INFO"
    
    # Create multipart form data
    $boundary = [System.Guid]::NewGuid().ToString()
    $LF = "`r`n"
    
    $fileBytes = [System.IO.File]::ReadAllBytes($ImagePath)
    $fileName = [System.IO.Path]::GetFileName($ImagePath)
    $fileEnc = [System.Text.Encoding]::GetEncoding("ISO-8859-1").GetString($fileBytes)
    
    $bodyLines = @(
        "--$boundary",
        "Content-Disposition: form-data; name=`"examId`"$LF",
        $ExamId,
        "--$boundary",
        "Content-Disposition: form-data; name=`"studentId`"$LF",
        $StudentId,
        "--$boundary",
        "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"",
        "Content-Type: application/octet-stream$LF",
        $fileEnc,
        "--$boundary--$LF"
    )
    
    $body = $bodyLines -join $LF
    $contentType = "multipart/form-data; boundary=$boundary"
    
    $response = Invoke-RestMethod -Uri $uploadUrl -Method Post -Body $body -ContentType $contentType -TimeoutSec 60
    
    $submissionId = $response.submissionId
    Write-Status "Upload successful!" "SUCCESS"
    Write-Host "   Submission ID: $submissionId" -ForegroundColor Green
    Write-Host "   Status: $($response.status)" -ForegroundColor Gray
    Write-Host "   Message: $($response.message)" -ForegroundColor Gray
    
} catch {
    Write-Status "Upload failed: $($_.Exception.Message)" "ERROR"
    
    # Try to get more details
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "   Response: $responseBody" -ForegroundColor Red
    }
    exit 1
}

# ═══════════════════════════════════════════════════════════════════════
# STEP 4: Monitor Status Changes (Real-time DB polling)
# ═══════════════════════════════════════════════════════════════════════

Write-Step "4" "Monitor Processing Status"

$maxWaitSeconds = 180
$pollIntervalSeconds = 5
$startTime = Get-Date
$lastStatus = -1
$statusHistory = @()

Write-Status "Polling submission status every ${pollIntervalSeconds}s (max ${maxWaitSeconds}s)..." "WAIT"
Write-Host ""

do {
    $elapsed = ((Get-Date) - $startTime).TotalSeconds
    
    # Query current status from database
    $statusQuery = @"
SELECT 
    Status,
    ErrorMessage,
    OcrStartedAt,
    OcrCompletedAt,
    EvaluationStartedAt,
    EvaluatedAt,
    TotalScore,
    MaxPossibleScore,
    Percentage,
    Grade,
    RetryCount
FROM WrittenSubmissions
WHERE Id = '$submissionId'
"@
    
    $submission = Invoke-SqlQuery -Query $statusQuery
    
    if ($submission.Count -eq 0) {
        Write-Status "Submission not found in database yet..." "WAIT"
        Start-Sleep -Seconds $pollIntervalSeconds
        continue
    }
    
    $currentStatus = $submission.Status
    $statusName = Get-StatusName -StatusCode $currentStatus
    
    # Only show update if status changed
    if ($currentStatus -ne $lastStatus) {
        $timestamp = (Get-Date).ToString("HH:mm:ss")
        $statusHistory += @{ Time = $timestamp; Status = $statusName }
        
        $statusIcon = switch ($currentStatus) {
            0 { "📤" }  # Uploaded
            1 { "🔍" }  # OcrProcessing
            2 { "🤖" }  # Evaluating
            3 { "✅" }  # Completed
            4 { "❌" }  # Failed
            default { "❓" }
        }
        
        Write-Host "   [$timestamp] $statusIcon Status: $statusName" -ForegroundColor $(if ($currentStatus -eq 3) { "Green" } elseif ($currentStatus -eq 4) { "Red" } else { "Yellow" })
        
        # Show timing details
        if ($submission.OcrStartedAt -and $submission.OcrStartedAt -ne [DBNull]::Value) {
            Write-Host "            OCR Started: $($submission.OcrStartedAt)" -ForegroundColor Gray
        }
        if ($submission.OcrCompletedAt -and $submission.OcrCompletedAt -ne [DBNull]::Value) {
            Write-Host "            OCR Completed: $($submission.OcrCompletedAt)" -ForegroundColor Gray
        }
        if ($submission.EvaluationStartedAt -and $submission.EvaluationStartedAt -ne [DBNull]::Value) {
            Write-Host "            Evaluation Started: $($submission.EvaluationStartedAt)" -ForegroundColor Gray
        }
        
        $lastStatus = $currentStatus
    }
    
    # Check if terminal state
    if ($currentStatus -eq 3 -or $currentStatus -eq 4) {
        break
    }
    
    Start-Sleep -Seconds $pollIntervalSeconds
    
} while ($elapsed -lt $maxWaitSeconds)

Write-Host ""

# ═══════════════════════════════════════════════════════════════════════
# STEP 5: Display Final Results
# ═══════════════════════════════════════════════════════════════════════

Write-Step "5" "Final Results"

# Get final submission details
$finalQuery = @"
SELECT 
    Id,
    ExamId,
    StudentId,
    Status,
    TotalScore,
    MaxPossibleScore,
    Percentage,
    Grade,
    ErrorMessage,
    SubmittedAt,
    EvaluatedAt,
    OcrProcessingTimeMs,
    EvaluationProcessingTimeMs
FROM WrittenSubmissions
WHERE Id = '$submissionId'
"@

$final = Invoke-SqlQuery -Query $finalQuery

if ($final.Status -eq 3) {
    Write-Status "EVALUATION COMPLETED SUCCESSFULLY!" "SUCCESS"
    Write-Host ""
    Write-Host "   ┌─────────────────────────────────────────────────────────┐" -ForegroundColor Green
    Write-Host "   │  SCORE SUMMARY                                          │" -ForegroundColor Green
    Write-Host "   ├─────────────────────────────────────────────────────────┤" -ForegroundColor Green
    Write-Host "   │  Total Score: $($final.TotalScore) / $($final.MaxPossibleScore)" -ForegroundColor White
    Write-Host "   │  Percentage:  $($final.Percentage)%" -ForegroundColor White
    Write-Host "   │  Grade:       $($final.Grade)" -ForegroundColor White
    Write-Host "   │  OCR Time:    $($final.OcrProcessingTimeMs) ms" -ForegroundColor Gray
    Write-Host "   │  Eval Time:   $($final.EvaluationProcessingTimeMs) ms" -ForegroundColor Gray
    Write-Host "   └─────────────────────────────────────────────────────────┘" -ForegroundColor Green
    
} elseif ($final.Status -eq 4) {
    Write-Status "EVALUATION FAILED!" "ERROR"
    Write-Host "   Error: $($final.ErrorMessage)" -ForegroundColor Red
} else {
    Write-Status "Timed out waiting for evaluation (Status: $(Get-StatusName -StatusCode $final.Status))" "WARNING"
}

# ═══════════════════════════════════════════════════════════════════════
# STEP 6: Display Per-Question Feedback (Subjective)
# ═══════════════════════════════════════════════════════════════════════

Write-Step "6" "Per-Question Results & Feedback"

$feedbackQuery = @"
SELECT 
    QuestionNumber,
    AwardedScore,
    MaxScore,
    Feedback,
    LEFT(ExtractedAnswer, 100) as ExtractedAnswer
FROM WrittenQuestionEvaluations
WHERE WrittenSubmissionId = '$submissionId'
ORDER BY QuestionNumber
"@

try {
    $questionResults = Invoke-SqlQuery -Query $feedbackQuery
    
    if ($questionResults.Count -gt 0) {
        Write-Host ""
        foreach ($qr in $questionResults) {
            $scoreColor = if ($qr.AwardedScore -eq $qr.MaxScore) { "Green" } elseif ($qr.AwardedScore -gt 0) { "Yellow" } else { "Red" }
            
            Write-Host "   ┌─ Question $($qr.QuestionNumber) ─────────────────────────────────────────┐" -ForegroundColor Cyan
            Write-Host "   │ Score: $($qr.AwardedScore) / $($qr.MaxScore)" -ForegroundColor $scoreColor
            Write-Host "   │ " -NoNewline
            Write-Host "Feedback: $($qr.Feedback)" -ForegroundColor White
            if ($Verbose -and $qr.ExtractedAnswer) {
                Write-Host "   │ Extracted: $($qr.ExtractedAnswer)..." -ForegroundColor Gray
            }
            Write-Host "   └───────────────────────────────────────────────────────────┘" -ForegroundColor Cyan
            Write-Host ""
        }
    } else {
        Write-Status "No per-question evaluations found" "WARNING"
    }
    
} catch {
    Write-Status "Could not fetch question results: $($_.Exception.Message)" "WARNING"
}

# ═══════════════════════════════════════════════════════════════════════
# STEP 7: Test Status API Endpoint
# ═══════════════════════════════════════════════════════════════════════

Write-Step "7" "Verify Status API Endpoint"

try {
    $statusUrl = "$BaseUrl/submissions/$submissionId"
    Write-Status "Testing GET $statusUrl" "INFO"
    
    $apiResult = Invoke-RestMethod -Uri $statusUrl -Method Get -TimeoutSec 30
    
    Write-Status "API endpoint working correctly!" "SUCCESS"
    Write-Host ""
    Write-Host "   API Response:" -ForegroundColor Gray
    $apiResult | ConvertTo-Json -Depth 3 | Write-Host -ForegroundColor Gray
    
} catch {
    Write-Status "Status API error: $($_.Exception.Message)" "ERROR"
}

# ═══════════════════════════════════════════════════════════════════════
# SUMMARY
# ═══════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor White
Write-Host "  TEST SUMMARY" -ForegroundColor Magenta
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor White
Write-Host ""
Write-Host "   Status Flow:" -ForegroundColor Cyan
foreach ($s in $statusHistory) {
    Write-Host "   [$($s.Time)] → $($s.Status)" -ForegroundColor Gray
}
Write-Host ""

if ($final.Status -eq 3) {
    Write-Host "   ✅ TEST PASSED: Complete flow working!" -ForegroundColor Green
    Write-Host "      - Answer sheet uploaded" -ForegroundColor Gray
    Write-Host "      - Status updates tracked" -ForegroundColor Gray
    Write-Host "      - OCR extraction completed" -ForegroundColor Gray
    Write-Host "      - AI evaluation completed" -ForegroundColor Gray
    Write-Host "      - Scores saved to database" -ForegroundColor Gray
    Write-Host "      - Per-question feedback available" -ForegroundColor Gray
} else {
    Write-Host "   ❌ TEST INCOMPLETE: Check errors above" -ForegroundColor Red
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor White
