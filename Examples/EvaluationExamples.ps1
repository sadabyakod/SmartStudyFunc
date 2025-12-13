# ================================================================
# SmartStudy AI Evaluation System - Example API Requests
# ================================================================
# BASE URL: Replace with your Azure Function App URL
# ================================================================

$BaseUrl = "https://your-function-app.azurewebsites.net/api"
$FunctionKey = "your-function-key-here"

# ================================================================
# 1. UPLOAD ANSWER (with OCR extraction)
# ================================================================
Write-Host "`n=== 1. Upload Answer with OCR ===" -ForegroundColor Cyan

$uploadUrl = "$BaseUrl/answers/upload?code=$FunctionKey"
$examId = 101
$questionId = 5
$pdfFilePath = "C:\path\to\student_answer.pdf"

# Create multipart form data
$form = @{
    examId = $examId
    questionId = $questionId
    file = Get-Item -Path $pdfFilePath
}

try {
    $uploadResponse = Invoke-RestMethod -Uri $uploadUrl -Method Post -Form $form
    Write-Host "✓ Upload successful!" -ForegroundColor Green
    Write-Host "Extracted Text Length: $($uploadResponse.extractedText.Length) characters"
    Write-Host "Blob Path: $($uploadResponse.blobPath)"
    Write-Host "Extracted Text Preview: $($uploadResponse.extractedText.Substring(0, [Math]::Min(200, $uploadResponse.extractedText.Length)))..."
    
    $blobPath = $uploadResponse.blobPath
    $extractedText = $uploadResponse.extractedText
}
catch {
    Write-Host "✗ Upload failed: $_" -ForegroundColor Red
    exit 1
}

# ================================================================
# 2. EVALUATE ANSWER (AI Scoring)
# ================================================================
Write-Host "`n=== 2. Evaluate Answer with AI ===" -ForegroundColor Cyan

$evaluateUrl = "$BaseUrl/answers/evaluate?code=$FunctionKey"

$evaluateRequest = @{
    examId = $examId
    questionId = $questionId
    studentAnswerText = $extractedText
    blobPath = $blobPath
} | ConvertTo-Json

try {
    $evaluateResponse = Invoke-RestMethod -Uri $evaluateUrl -Method Post -Body $evaluateRequest -ContentType "application/json"
    Write-Host "✓ Evaluation successful!" -ForegroundColor Green
    Write-Host "Score: $($evaluateResponse.score)/$($evaluateResponse.maxMarks) ($($evaluateResponse.percentage)%)"
    Write-Host "Feedback: $($evaluateResponse.feedback)"
    Write-Host "Strengths: $($evaluateResponse.strengths)"
    Write-Host "Improvements: $($evaluateResponse.improvements)"
    Write-Host "Keywords Matched: $($evaluateResponse.keywordsMatched -join ', ')"
    Write-Host "Missing Keywords: $($evaluateResponse.missingKeywords -join ', ')"
    Write-Host "Used Fallback: $($evaluateResponse.usedFallback)"
}
catch {
    Write-Host "✗ Evaluation failed: $_" -ForegroundColor Red
    exit 1
}

# ================================================================
# 3. BATCH EVALUATE (Multiple Answers)
# ================================================================
Write-Host "`n=== 3. Batch Evaluate (3 answers) ===" -ForegroundColor Cyan

$batchUrl = "$BaseUrl/answers/evaluate/batch?code=$FunctionKey"

$batchRequest = @{
    evaluations = @(
        @{
            examId = 101
            questionId = 1
            studentAnswerText = "The Pythagorean theorem states that a² + b² = c² where c is the hypotenuse."
        },
        @{
            examId = 101
            questionId = 2
            studentAnswerText = "Differentiation is the process of finding the rate of change. d/dx(x²) = 2x"
        },
        @{
            examId = 101
            questionId = 3
            studentAnswerText = "The area of a circle is πr² where r is the radius of the circle."
        }
    )
} | ConvertTo-Json -Depth 5

try {
    $batchResponse = Invoke-RestMethod -Uri $batchUrl -Method Post -Body $batchRequest -ContentType "application/json"
    Write-Host "✓ Batch evaluation successful!" -ForegroundColor Green
    Write-Host "Total Processed: $($batchResponse.totalProcessed)/$($batchResponse.totalRequested)"
    
    foreach ($result in $batchResponse.results) {
        Write-Host "`nQuestion $($result.questionId): $($result.score)/$($result.maxMarks) ($($result.percentage)%)"
        Write-Host "  Feedback: $($result.feedback)"
    }
}
catch {
    Write-Host "✗ Batch evaluation failed: $_" -ForegroundColor Red
    exit 1
}

# ================================================================
# 4. UPLOAD + EVALUATE (Combined Workflow)
# ================================================================
Write-Host "`n=== 4. Combined Upload + Evaluate Workflow ===" -ForegroundColor Cyan

function Process-StudentAnswer {
    param(
        [int]$ExamId,
        [int]$QuestionId,
        [string]$FilePath
    )
    
    # Step 1: Upload
    $uploadUrl = "$BaseUrl/answers/upload?code=$FunctionKey"
    $uploadForm = @{
        examId = $ExamId
        questionId = $QuestionId
        file = Get-Item -Path $FilePath
    }
    
    $uploadResult = Invoke-RestMethod -Uri $uploadUrl -Method Post -Form $uploadForm
    Write-Host "✓ Uploaded: $($uploadResult.blobPath)" -ForegroundColor Green
    
    # Step 2: Evaluate
    $evaluateUrl = "$BaseUrl/answers/evaluate?code=$FunctionKey"
    $evaluateBody = @{
        examId = $ExamId
        questionId = $QuestionId
        studentAnswerText = $uploadResult.extractedText
        extractedText = $uploadResult.extractedText
        blobPath = $uploadResult.blobPath
    } | ConvertTo-Json
    
    $evaluateResult = Invoke-RestMethod -Uri $evaluateUrl -Method Post -Body $evaluateBody -ContentType "application/json"
    Write-Host "✓ Evaluated: $($evaluateResult.score)/$($evaluateResult.maxMarks)" -ForegroundColor Green
    
    return $evaluateResult
}

try {
    $result = Process-StudentAnswer -ExamId 101 -QuestionId 5 -FilePath "C:\path\to\answer.pdf"
    Write-Host "`nFinal Score: $($result.score)/$($result.maxMarks) ($($result.percentage)%)"
}
catch {
    Write-Host "✗ Process failed: $_" -ForegroundColor Red
}

Write-Host "`n=== All Examples Complete! ===" -ForegroundColor Cyan
