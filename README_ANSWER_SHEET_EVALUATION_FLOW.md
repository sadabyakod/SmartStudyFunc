# Answer Sheet Evaluation Flow - End-to-End Backend API Reference

## Overview

The SmartStudy Answer Sheet Evaluation System provides automated grading of handwritten student answer sheets using OCR (Google Cloud Vision) and AI evaluation (Azure OpenAI GPT-4). This document covers the complete end-to-end flow from answer sheet upload to result retrieval.

---

## 🔄 Complete Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│                           ANSWER SHEET EVALUATION FLOW                                  │
└─────────────────────────────────────────────────────────────────────────────────────────┘

┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌──────────────┐
│   UPLOAD     │───▶│   QUEUE      │───▶│     OCR      │───▶│  EVALUATE    │───▶│   RESULT     │
│  Status: 0   │    │  (Trigger)   │    │  Status: 1   │    │  Status: 2   │    │  Status: 3   │
└──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘    └──────────────┘
       │                                       │                   │                   │
       ▼                                       ▼                   ▼                   ▼
  ┌─────────┐                           ┌───────────┐       ┌───────────┐       ┌───────────┐
  │  Blob   │                           │  Google   │       │  Azure    │       │   Blob    │
  │ Storage │                           │  Vision   │       │  OpenAI   │       │  Storage  │
  │ (Images)│                           │   API     │       │  GPT-4    │       │  (JSON)   │
  └─────────┘                           └───────────┘       └───────────┘       └───────────┘
```

---

## 📊 Status Codes Reference

| Status | Name        | Description                                      | Database Column Updated     |
|--------|-------------|--------------------------------------------------|-----------------------------|
| 0      | Uploaded    | Answer sheet uploaded, waiting in queue          | `SubmittedAt`               |
| 1      | OcrProcessing| Google Vision OCR extracting text               | `OcrStartedAt`              |
| 2      | Evaluating  | AI evaluating answers against rubrics            | `EvaluationStartedAt`       |
| 3      | Completed   | Evaluation complete, results available           | `EvaluatedAt`, `TotalScore` |
| 4      | Failed      | Processing failed (see `ErrorMessage`)           | `ErrorMessage`              |

---

## 🌐 API Endpoints

### Base URL
```
Production: https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net
```

### Authentication
All endpoints require a function key passed as query parameter:
```
?code={FUNCTION_KEY}
```

---

## 📋 Endpoint Details

### 1. Upload Answer Sheet (Image Upload)

**Endpoint:** `POST /api/answers/upload`

**Content-Type:** `multipart/form-data`

**Request:**
```
Form Fields:
- examId (string, required): Exam identifier
- studentId (string, optional): Student identifier (default: "anonymous")
- files (file, required): Answer sheet image(s) - PDF, JPG, JPEG, PNG (max 10MB)
```

**cURL Example:**
```bash
curl -X POST "https://smartstudy-func.../api/answers/upload?code=YOUR_KEY" \
  -F "examId=SAMPLE-EXAM-001" \
  -F "studentId=STU-12345" \
  -F "files=@answer-sheet.jpg"
```

**PowerShell Example:**
```powershell
$form = @{
    examId = "SAMPLE-EXAM-001"
    studentId = "STU-12345"
}
$files = @{
    files = Get-Item ".\answer-sheet.jpg"
}
Invoke-RestMethod -Uri "$BaseUrl/api/answers/upload?code=$Key" `
    -Method POST -Form $form -Files $files
```

**Response (200 OK):**
```json
{
  "submissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "status": 0,
  "statusText": "Uploaded",
  "message": "Answer sheet uploaded successfully. Processing will begin shortly.",
  "filePaths": [
    "https://stsmartstudydev.blob.core.windows.net/student-answers/SAMPLE-EXAM-001/..."
  ],
  "submittedAt": "2025-12-15T10:00:00Z"
}
```

**Error Responses:**
- `400 Bad Request`: Missing examId, invalid file type, file too large
- `500 Internal Server Error`: Upload or queue failure

---

### 2. Direct Text Evaluation (No OCR)

**Endpoint:** `POST /api/answers/evaluate`

**Content-Type:** `application/json`

**Use Case:** When student answer is already in text format (typed answers, copy-paste)

**Request:**
```json
{
  "examId": "SAMPLE-EXAM-001",
  "questionId": "2657ea0b-1ea3-4e85-8c37-6710e70d954d",
  "studentAnswerText": "Photosynthesis is the process by which plants use sunlight to convert carbon dioxide and water into glucose and oxygen. The process occurs in the chloroplasts using chlorophyll pigment.",
  "studentId": "STU-12345"
}
```

**cURL Example:**
```bash
curl -X POST "https://smartstudy-func.../api/answers/evaluate?code=YOUR_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "examId": "SAMPLE-EXAM-001",
    "questionId": "2657ea0b-1ea3-4e85-8c37-6710e70d954d",
    "studentAnswerText": "Photosynthesis is..."
  }'
```

**Response (200 OK):**
```json
{
  "success": true,
  "evaluationId": "eval-uuid",
  "submissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "examId": "SAMPLE-EXAM-001",
  "questionId": "2657ea0b-1ea3-4e85-8c37-6710e70d954d",
  "questionNumber": 1,
  "questionText": "Explain the process of photosynthesis in detail.",
  "maxScore": 20.0,
  "awardedScore": 9.5,
  "percentage": 47.5,
  "feedback": "Good understanding of the basic concept. Missing details about light-dependent and light-independent reactions.",
  "rubricBreakdown": {
    "steps": [
      {
        "stepNumber": 1,
        "concept": "Definition of photosynthesis",
        "maxMarks": 3,
        "awardedMarks": 3,
        "status": "Complete",
        "matchedKeywords": ["process", "plants", "sunlight"]
      },
      {
        "stepNumber": 2,
        "concept": "Role of chlorophyll",
        "maxMarks": 3,
        "awardedMarks": 2,
        "status": "Partial",
        "matchedKeywords": ["chloroplasts", "chlorophyll"]
      },
      {
        "stepNumber": 3,
        "concept": "Chemical equation",
        "maxMarks": 5,
        "awardedMarks": 0,
        "status": "Missing",
        "matchedKeywords": []
      }
    ]
  },
  "modelAnswer": "Photosynthesis is the process where green plants...",
  "evaluatedAt": "2025-12-15T10:05:00Z"
}
```

---

### 3. Batch Evaluation (Multiple Questions)

**Endpoint:** `POST /api/answers/batch-evaluate`

**Content-Type:** `application/json`

**Request:**
```json
{
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "answers": [
    {
      "questionId": "q1-uuid",
      "answerText": "Answer to question 1..."
    },
    {
      "questionId": "q2-uuid",
      "answerText": "Answer to question 2..."
    },
    {
      "questionId": "q3-uuid",
      "answerText": "Answer to question 3..."
    }
  ]
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "submissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "totalScore": 45.5,
  "maxPossibleScore": 100.0,
  "percentage": 45.5,
  "grade": "F",
  "evaluations": [
    {
      "questionId": "q1-uuid",
      "questionNumber": 1,
      "awardedScore": 15.5,
      "maxScore": 30.0,
      "feedback": "..."
    },
    {
      "questionId": "q2-uuid",
      "questionNumber": 2,
      "awardedScore": 18.0,
      "maxScore": 40.0,
      "feedback": "..."
    },
    {
      "questionId": "q3-uuid",
      "questionNumber": 3,
      "awardedScore": 12.0,
      "maxScore": 30.0,
      "feedback": "..."
    }
  ],
  "evaluatedAt": "2025-12-15T10:10:00Z"
}
```

---

### 4. Get Submission Status

**Endpoint:** `GET /api/submissions/{submissionId}/status`

**Use Case:** Poll this endpoint to track processing progress

**Response (200 OK):**
```json
{
  "submissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "status": 3,
  "statusText": "Completed",
  "totalScore": 45.5,
  "maxPossibleScore": 100.0,
  "percentage": 45.5,
  "grade": "F",
  "evaluationResultBlobPath": "evaluation-results/SAMPLE-EXAM-001/a1b2.../evaluation-result.json",
  "timestamps": {
    "submittedAt": "2025-12-15T10:00:00Z",
    "ocrStartedAt": "2025-12-15T10:00:05Z",
    "ocrCompletedAt": "2025-12-15T10:00:15Z",
    "evaluationStartedAt": "2025-12-15T10:00:16Z",
    "evaluatedAt": "2025-12-15T10:01:30Z"
  },
  "processingTime": {
    "ocrMs": 10000,
    "evaluationMs": 74000,
    "totalMs": 90000
  }
}
```

**Status-Specific Responses:**

```json
// Status 0 - Uploaded (waiting in queue)
{
  "status": 0,
  "statusText": "Uploaded",
  "message": "Answer sheet is queued for processing"
}

// Status 1 - OCR Processing
{
  "status": 1,
  "statusText": "OcrProcessing",
  "message": "Extracting text from answer sheet images"
}

// Status 2 - Evaluating
{
  "status": 2,
  "statusText": "Evaluating",
  "message": "AI is evaluating answers against rubrics"
}

// Status 4 - Failed
{
  "status": 4,
  "statusText": "Failed",
  "errorMessage": "OCR service failed: Unable to read handwriting",
  "retryCount": 3
}
```

---

### 5. Get Evaluation Results (Detailed)

**Endpoint:** `GET /api/evaluations/{submissionId}/result`

**Response (200 OK):**
```json
{
  "writtenSubmissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "totalScore": 45.5,
  "maxPossibleScore": 100.0,
  "percentage": 45.5,
  "grade": "F",
  "evaluatedAt": "2025-12-15T10:01:30Z",
  "questionEvaluations": [
    {
      "id": "eval-uuid-1",
      "questionId": "q1-uuid",
      "questionNumber": 1,
      "questionText": "Explain the process of photosynthesis in detail.",
      "extractedAnswer": "Photosynthesis is the process by which plants...",
      "modelAnswer": "Photosynthesis is the biological process where...",
      "maxScore": 20.0,
      "awardedScore": 9.5,
      "feedback": "Good basic understanding. Missing chemical equation and detailed steps.",
      "rubricBreakdown": "Step 1: Definition (3/3) ✓\nStep 2: Chlorophyll (2/3) ~\nStep 3: Equation (0/5) ✗\nStep 4: Products (3/4) ~\nStep 5: Importance (1.5/5) ~",
      "stepwiseMarking": [
        {
          "stepNumber": 1,
          "concept": "Definition",
          "maxMarks": 3,
          "awardedMarks": 3,
          "status": "Complete",
          "keywords": ["process", "plants", "sunlight", "food"]
        },
        {
          "stepNumber": 2,
          "concept": "Role of Chlorophyll",
          "maxMarks": 3,
          "awardedMarks": 2,
          "status": "Partial",
          "keywords": ["chlorophyll", "green pigment"]
        },
        {
          "stepNumber": 3,
          "concept": "Chemical Equation",
          "maxMarks": 5,
          "awardedMarks": 0,
          "status": "Missing",
          "keywords": []
        }
      ],
      "evaluatedAt": "2025-12-15T10:01:30Z"
    },
    {
      "id": "eval-uuid-2",
      "questionId": "q2-uuid",
      "questionNumber": 2,
      "questionText": "State and explain Newton's three laws of motion.",
      "extractedAnswer": "Newton's first law states...",
      "modelAnswer": "Newton's Three Laws of Motion...",
      "maxScore": 15.0,
      "awardedScore": 5.8,
      "feedback": "Only first law explained. Missing second and third laws.",
      "rubricBreakdown": "Law 1 (3/5) ~\nLaw 2 (0/5) ✗\nLaw 3 (2.8/5) ~",
      "evaluatedAt": "2025-12-15T10:01:30Z"
    }
  ]
}
```

---

### 6. Get Evaluation Download URL (SAS Token)

**Endpoint:** `GET /api/evaluations/{submissionId}/download-url`

**Use Case:** Get direct blob access for mobile apps to download JSON

**Response (200 OK):**
```json
{
  "submissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "downloadUrl": "https://stsmartstudydev.blob.core.windows.net/evaluation-results/SAMPLE-EXAM-001/a1b2.../evaluation-result.json?sv=2021-06-08&se=2025-12-16T10%3A00%3A00Z&sr=b&sp=r&sig=...",
  "expiresAt": "2025-12-16T10:00:00Z",
  "contentType": "application/json"
}
```

---

### 7. Get Evaluations by Exam

**Endpoint:** `GET /api/exams/{examId}/evaluations`

**Query Parameters:**
- `page` (int, default: 1): Page number
- `pageSize` (int, default: 20): Results per page
- `status` (int, optional): Filter by status

**Response (200 OK):**
```json
{
  "examId": "SAMPLE-EXAM-001",
  "totalCount": 150,
  "page": 1,
  "pageSize": 20,
  "submissions": [
    {
      "submissionId": "uuid-1",
      "studentId": "STU-001",
      "status": 3,
      "totalScore": 78.5,
      "percentage": 78.5,
      "grade": "B",
      "submittedAt": "2025-12-15T09:00:00Z",
      "evaluatedAt": "2025-12-15T09:05:00Z"
    },
    {
      "submissionId": "uuid-2",
      "studentId": "STU-002",
      "status": 3,
      "totalScore": 65.0,
      "percentage": 65.0,
      "grade": "C",
      "submittedAt": "2025-12-15T09:10:00Z",
      "evaluatedAt": "2025-12-15T09:15:00Z"
    }
  ]
}
```

---

### 8. Health Check

**Endpoint:** `GET /api/health`

**Response (200 OK):**
```json
{
  "status": "running",
  "timestamp": "2025-12-15T10:00:00Z",
  "uptime": "5d 12h 30m",
  "memory_mb": 256,
  "sql_configured": true,
  "openai_configured": true,
  "database": "connected",
  "response_time_ms": 45.5,
  "version": "1.0.0"
}
```

---

## 🔄 Processing Pipeline Details

### Phase 1: Upload (UploadAnswer Function)
```
Input: Image file(s) + examId + studentId
Output: submissionId, Status=0

Steps:
1. Validate file type (PDF, JPG, JPEG, PNG)
2. Validate file size (max 10MB)
3. Upload to blob storage (student-answers container)
4. Create database record (Status=0, SubmittedAt=now)
5. Add message to processing queue
6. Return submissionId to client
```

### Phase 2: OCR Processing (ProcessWrittenSubmission Function)
```
Trigger: Queue message from written-submission-processing queue
Output: Extracted text, Status=1→(continues)

Steps:
1. Update Status=1 (OcrProcessing), OcrStartedAt=now
2. Download image(s) from blob storage
3. Send to Google Cloud Vision API (DOCUMENT_TEXT_DETECTION)
4. Receive extracted text with confidence score
5. If text > 100KB: Save to blob (ocr-extracted-text container)
6. Save extracted text to database
7. Update OcrCompletedAt timestamp
```

### Phase 3: AI Evaluation (ProcessWrittenSubmission Function continues)
```
Input: Extracted text + Exam questions with rubrics
Output: Evaluation results, Status=2→3

Steps:
1. Update Status=2 (Evaluating), EvaluationStartedAt=now
2. Fetch exam questions with model answers and rubrics
3. For each question:
   a. Match student answer section to question
   b. Call Azure OpenAI GPT-4 for evaluation
   c. Apply step-wise marking scheme
   d. Calculate awarded marks per step
   e. Generate feedback
4. Calculate total score, percentage, grade
5. Save evaluation results to blob (evaluation-results container)
6. Save results to database (WrittenQuestionEvaluations table)
7. Update Status=3 (Completed), EvaluatedAt=now
```

### Phase 4: Result Storage
```
Blob Storage:
- Container: evaluation-results
- Path: {examId}/{submissionId}/evaluation-result.json
- Content: Full evaluation JSON with all question breakdowns
- Retention: Permanent (never auto-deleted)

Database:
- WrittenSubmissions: Summary (score, grade, status)
- WrittenQuestionEvaluations: Per-question details
- EvaluationResultBlobPath: Link to blob JSON
```

---

## 📊 Database Schema

### WrittenSubmissions Table
```sql
CREATE TABLE WrittenSubmissions (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    ExamId NVARCHAR(200) NOT NULL,
    StudentId NVARCHAR(200) NOT NULL,
    FilePaths NVARCHAR(MAX) NOT NULL,           -- JSON array of blob URLs
    Status INT NOT NULL DEFAULT 0,               -- 0-4
    
    -- OCR Results
    ExtractedText NVARCHAR(MAX),
    ExtractedTextJson NVARCHAR(MAX),
    ExtractedTextBlobPath NVARCHAR(500),
    
    -- Evaluation Results
    TotalScore DECIMAL(10,2),
    MaxPossibleScore DECIMAL(10,2),
    Percentage DECIMAL(5,2),
    Grade NVARCHAR(10),
    EvaluationResultBlobPath NVARCHAR(500),      -- Permanent result storage
    
    -- Error Handling
    ErrorMessage NVARCHAR(MAX),
    RetryCount INT DEFAULT 0,
    
    -- Timestamps
    SubmittedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    OcrStartedAt DATETIME2,
    OcrCompletedAt DATETIME2,
    EvaluationStartedAt DATETIME2,
    EvaluatedAt DATETIME2,
    
    -- Processing Metrics
    OcrProcessingTimeMs BIGINT,
    EvaluationProcessingTimeMs BIGINT,
    
    -- Cleanup
    BlobsDeleted BIT DEFAULT 0,
    
    -- Indexes
    INDEX IX_WrittenSubmissions_ExamId (ExamId),
    INDEX IX_WrittenSubmissions_StudentId (StudentId),
    INDEX IX_WrittenSubmissions_Status (Status)
);
```

### WrittenQuestionEvaluations Table
```sql
CREATE TABLE WrittenQuestionEvaluations (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    WrittenSubmissionId UNIQUEIDENTIFIER NOT NULL,
    QuestionId UNIQUEIDENTIFIER NOT NULL,
    QuestionNumber INT NOT NULL,
    
    ExtractedAnswer NVARCHAR(MAX),
    ModelAnswer NVARCHAR(MAX),
    
    MaxScore DECIMAL(10,2) NOT NULL,
    AwardedScore DECIMAL(10,2) NOT NULL,
    
    Feedback NVARCHAR(MAX),
    RubricBreakdown NVARCHAR(MAX),               -- Step-wise marking JSON
    
    EvaluatedAt DATETIME2 NOT NULL,
    
    FOREIGN KEY (WrittenSubmissionId) REFERENCES WrittenSubmissions(Id)
);
```

---

## 🔧 Configuration

### Required Environment Variables

```bash
# Azure Storage
AzureWebJobsStorage=DefaultEndpointsProtocol=https;AccountName=...

# SQL Database
SqlConnectionString=Server=...;Database=...;User ID=...;Password=...

# Azure OpenAI
AzureOpenAI__Endpoint=https://your-openai.openai.azure.com/
AzureOpenAI__ApiKey=your-api-key
AzureOpenAI__ChatDeployment=gpt-4o-mini
AzureOpenAI__EmbeddingDeployment=text-embedding-3-small

# Google Cloud Vision (OCR)
GoogleCloud__ApiKey=your-google-vision-api-key
```

---

## 🧪 Testing Examples

### Complete Flow Test (PowerShell)

```powershell
$BaseUrl = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
$Key = "YOUR_FUNCTION_KEY"

# 1. Submit text answer for evaluation
$body = @{
    examId = "SAMPLE-EXAM-001"
    questionId = "2657ea0b-1ea3-4e85-8c37-6710e70d954d"
    studentAnswerText = @"
Photosynthesis is the process by which green plants use sunlight to make food.
Plants use chlorophyll in their leaves to capture light energy.
The process takes carbon dioxide from the air and water from the soil.
These are converted into glucose (sugar) and oxygen.
"@
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri "$BaseUrl/api/answers/evaluate?code=$Key" `
    -Method POST -Body $body -ContentType "application/json" -TimeoutSec 120

Write-Host "Score: $($result.awardedScore)/$($result.maxScore)"
Write-Host "Grade: $(if($result.percentage -ge 90){'A'}elseif($result.percentage -ge 75){'B'}else{'F'})"
Write-Host "Feedback: $($result.feedback)"

# 2. Check submission status
$status = Invoke-RestMethod -Uri "$BaseUrl/api/submissions/$($result.submissionId)/status?code=$Key"
Write-Host "Status: $($status.statusText)"

# 3. Get detailed results
$details = Invoke-RestMethod -Uri "$BaseUrl/api/evaluations/$($result.submissionId)/result?code=$Key"
$details.questionEvaluations | ForEach-Object {
    Write-Host "Q$($_.questionNumber): $($_.awardedScore)/$($_.maxScore) - $($_.feedback)"
}
```

---

## 📈 Grading Scale

| Percentage | Grade |
|------------|-------|
| 90-100%    | A     |
| 75-89%     | B     |
| 60-74%     | C     |
| 50-59%     | D     |
| 0-49%      | F     |

---

## ⚠️ Error Handling

### Common Error Responses

```json
// 400 Bad Request - Validation Error
{
  "error": "examId is required",
  "code": "VALIDATION_ERROR"
}

// 404 Not Found - Submission not found
{
  "error": "Submission not found",
  "submissionId": "invalid-uuid",
  "code": "NOT_FOUND"
}

// 500 Internal Server Error - Processing failure
{
  "error": "OCR service failed",
  "details": "Google Vision API rate limit exceeded",
  "code": "OCR_ERROR",
  "retryable": true
}

// 503 Service Unavailable - Dependency failure
{
  "error": "Azure OpenAI service unavailable",
  "code": "SERVICE_UNAVAILABLE",
  "retryAfter": 60
}
```

---

## 🔒 Security

### API Key Management
- Function keys are required for all endpoints
- Keys are stored in Azure Key Vault (production)
- Rotate keys every 90 days

### Data Protection
- All data encrypted at rest (Azure Storage encryption)
- TLS 1.2+ for all API calls
- Student data retention: 30 days for images, permanent for results

### Rate Limiting
- 100 requests/minute per function key
- 10 concurrent evaluations per student
- Large file uploads: 5 requests/minute

---

## 📞 Support

### Monitoring
- Application Insights for logs and metrics
- Azure Monitor alerts for failures
- Dashboard: Azure Portal → smartstudy-func → Monitor

### Troubleshooting
1. Check `/api/health` endpoint
2. Review Application Insights logs
3. Verify database connectivity
4. Check Google Vision API quotas
5. Verify Azure OpenAI deployment status

### Contact
- Backend Issues: Check Azure Function logs
- API Issues: Verify function keys and request format
- Evaluation Issues: Review exam questions and rubrics setup
