# Answer Sheet Evaluation Flow - End-to-End Backend API Reference

## 📋 Table of Contents
1. [Overview](#overview)
2. [Complete Workflow Diagram](#complete-workflow-diagram)
3. [Phase-by-Phase Breakdown](#phase-by-phase-breakdown)
4. [Status Transitions](#status-transitions)
5. [API Endpoints Reference](#api-endpoints-reference)
6. [Data Flow](#data-flow)
7. [Error Handling](#error-handling)
8. [Testing Guide](#testing-guide)

---

## Overview

The Answer Sheet Evaluation system processes handwritten answer sheets through OCR and AI evaluation to provide detailed, step-wise marking with feedback. The system is fully asynchronous, scalable, and fault-tolerant.

**Key Technologies:**
- **Azure Functions** - Serverless compute
- **Azure Blob Storage** - File and result storage
- **Azure Queue Storage** - Asynchronous processing
- **Google Cloud Vision API** - OCR for handwritten text
- **Azure OpenAI (GPT-4)** - AI evaluation with rubrics
- **Azure SQL Database** - Metadata and state tracking

---

## Complete Workflow Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          STUDENT SUBMISSION                              │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Phase 1: File Upload & Storage                                         │
│  ────────────────────────────────────────────────────────────────────── │
│  Endpoint: POST /api/answers/upload                                     │
│  • Upload answer sheet images (JPG/PNG/PDF)                             │
│  • Store in Azure Blob Storage (textbooks/written-answers/)             │
│  • Create WrittenSubmission record in database                          │
│  • Status: 0 (Uploaded)                                                 │
│  • Generate unique Submission ID                                        │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Phase 2: Queue Message Creation                                        │
│  ────────────────────────────────────────────────────────────────────── │
│  • Create queue message in written-submission-processing                │
│  • Message contains: submissionId, examId, studentId, filePaths         │
│  • Priority: normal/high                                                │
│  • Retry count: 0                                                       │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Phase 3: OCR Processing (ProcessWrittenSubmission Function)            │
│  ────────────────────────────────────────────────────────────────────── │
│  Trigger: Queue message in written-submission-processing                │
│                                                                          │
│  Step 1: Fetch Submission from Database                                 │
│  • Query WrittenSubmissions by ID                                       │
│  • Validate Status (skip if already Completed/Evaluating)               │
│                                                                          │
│  Step 2: Update Status to OCR Processing                                │
│  • Status: 0 → 1 (OcrProcessing)                                        │
│  • Set OcrStartedAt timestamp                                           │
│                                                                          │
│  Step 3: Extract Text via Google Cloud Vision API                       │
│  • Download images from blob storage                                    │
│  • Call DOCUMENT_TEXT_DETECTION API                                     │
│  • Process each page/image                                              │
│  • Extract handwritten text with confidence scores                      │
│                                                                          │
│  Step 4: Save Extracted Text                                            │
│  • Store text in database (ExtractedText column)                        │
│  • If text > 100KB, save to blob: ocr-extracted-text/{examId}/...      │
│  • Save ExtractedTextBlobPath reference                                 │
│  • Set OcrCompletedAt timestamp                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Phase 4: AI Evaluation (ProcessWrittenSubmission continues)            │
│  ────────────────────────────────────────────────────────────────────── │
│  Step 1: Update Status to Evaluating                                    │
│  • Status: 1 → 2 (Evaluating)                                           │
│  • Set EvaluationStartedAt timestamp                                    │
│                                                                          │
│  Step 2: Fetch Exam Questions with Rubrics                              │
│  • Query ExamQuestions for examId                                       │
│  • Load question text, model answers, rubrics, keywords                 │
│  • Get max scores for each question                                     │
│                                                                          │
│  Step 3: AI Evaluation with Azure OpenAI (GPT-4)                        │
│  • For each question:                                                   │
│    - Extract relevant answer from OCR text                              │
│    - Compare with model answer using rubric                             │
│    - Generate step-wise marking breakdown                               │
│    - Award marks based on completeness                                  │
│    - Generate detailed feedback                                         │
│  • Calculate total score, percentage, grade                             │
│                                                                          │
│  Step 4: Save Evaluation to Blob Storage (NEW!)                         │
│  • Serialize evaluation result to JSON                                  │
│  • Path: evaluation-results/{examId}/{submissionId}/evaluation-result.json │
│  • Set Content-Type: application/json                                   │
│  • Pretty-print for readability                                         │
│  • Store blob path in EvaluationResultBlobPath                          │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Phase 5: Save Results to Database                                      │
│  ────────────────────────────────────────────────────────────────────── │
│  Step 1: Update WrittenSubmissions Table                                │
│  • Status: 2 → 3 (Completed)                                            │
│  • Set EvaluatedAt timestamp                                            │
│  • Save TotalScore, MaxPossibleScore, Percentage, Grade                 │
│  • Save EvaluationResultBlobPath                                        │
│  • Save processing times (OcrProcessingTimeMs, EvaluationProcessingTimeMs) │
│                                                                          │
│  Step 2: Insert Question Evaluations                                    │
│  • Insert into WrittenQuestionEvaluations table                         │
│  • For each question: questionId, extractedAnswer, awardedScore         │
│  • Save feedback and rubricBreakdown                                    │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  Phase 6: Student Retrieval                                             │
│  ────────────────────────────────────────────────────────────────────── │
│  Option 1: Get Status                                                   │
│  • Endpoint: GET /api/submissions/{submissionId}/status                 │
│  • Returns: status, scores, grade, blob paths                           │
│                                                                          │
│  Option 2: Get Detailed Results (from Database)                         │
│  • Endpoint: GET /api/evaluations/{submissionId}/results                │
│  • Returns: Full evaluation with all questions                          │
│                                                                          │
│  Option 3: Get Result from Blob (NEW!)                                  │
│  • Endpoint: GET /api/evaluations/{submissionId}/result                 │
│  • Downloads JSON from blob storage                                     │
│  • Returns: Complete evaluation result                                  │
│                                                                          │
│  Option 4: Get Direct Download URL (NEW!)                               │
│  • Endpoint: GET /api/evaluations/{submissionId}/download-url           │
│  • Generates SAS token for direct blob access                           │
│  • Returns: Time-limited download URL (24h expiry)                      │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Phase-by-Phase Breakdown

### Phase 1: File Upload & Storage

**Function:** `UploadAnswer.cs`  
**Trigger:** HTTP POST  
**Endpoint:** `POST /api/answers/upload`

#### Request Format
```http
POST /api/answers/upload?code={function-key}
Content-Type: multipart/form-data

--boundary
Content-Disposition: form-data; name="examId"

SAMPLE-EXAM-001
--boundary
Content-Disposition: form-data; name="studentId"

STU-12345
--boundary
Content-Disposition: form-data; name="files"; filename="answer1.jpg"
Content-Type: image/jpeg

<binary image data>
--boundary--
```

#### Process Flow
1. **Validate Request**
   - Check content type is multipart/form-data
   - Validate examId and studentId present
   - Validate file uploaded

2. **Validate Files**
   - Check file extensions (.jpg, .jpeg, .png, .pdf)
   - Check file size (max 10MB per file)
   - Validate file not empty

3. **Generate IDs**
   ```csharp
   var submissionId = Guid.NewGuid();
   var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
   ```

4. **Upload to Blob Storage**
   ```csharp
   // Path format
   var blobPath = $"textbooks/written-answers/{examId}/{studentId}/{timestamp}_{uniqueId}.jpg";
   
   // Upload to container
   var containerClient = _blobServiceClient.GetBlobContainerClient("textbooks");
   var blobClient = containerClient.GetBlobClient(blobPath);
   await blobClient.UploadAsync(fileStream);
   ```

5. **Create Database Record**
   ```sql
   INSERT INTO WrittenSubmissions (
       Id, ExamId, StudentId, FilePaths, Status, SubmittedAt
   ) VALUES (
       @SubmissionId, @ExamId, @StudentId, @FilePaths, 0, GETUTCDATE()
   )
   ```

6. **Queue Processing Message**
   ```csharp
   var queueMessage = new {
       writtenSubmissionId = submissionId,
       examId = examId,
       studentId = studentId,
       filePaths = new[] { blobPath },
       submittedAt = DateTime.UtcNow,
       priority = "normal",
       retryCount = 0
   };
   
   await queueClient.SendMessageAsync(JsonSerializer.Serialize(queueMessage));
   ```

#### Response Format
```json
{
  "submissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "status": 0,
  "statusText": "Uploaded - Processing will begin shortly",
  "submittedAt": "2025-12-15T10:00:00Z",
  "message": "Answer sheet uploaded successfully. You will be notified when evaluation is complete."
}
```

---

### Phase 2 & 3: OCR Processing

**Function:** `ProcessWrittenSubmission.cs`  
**Trigger:** Queue message from `written-submission-processing`  
**Service:** `GoogleVisionOcrService.cs`

#### Process Flow

1. **Receive Queue Message**
   ```csharp
   [Function(nameof(ProcessWrittenSubmission))]
   public async Task Run(
       [QueueTrigger("written-submission-processing")] string messageText,
       FunctionContext context,
       CancellationToken cancellationToken)
   ```

2. **Parse Message**
   ```csharp
   var message = JsonSerializer.Deserialize<WrittenSubmissionProcessingMessage>(messageText);
   var submissionId = message.WrittenSubmissionId;
   var examId = message.ExamId;
   var filePaths = message.FilePaths;
   ```

3. **Fetch Submission from Database**
   ```sql
   SELECT Id, ExamId, StudentId, Status, FilePaths
   FROM WrittenSubmissions
   WHERE Id = @SubmissionId
   ```

4. **Update Status to OCR Processing**
   ```sql
   UPDATE WrittenSubmissions
   SET Status = 1, -- OcrProcessing
       OcrStartedAt = GETUTCDATE()
   WHERE Id = @SubmissionId
   ```

5. **Google Cloud Vision OCR**
   ```csharp
   // For each image in filePaths
   foreach (var blobPath in filePaths)
   {
       // Download image from blob
       var stream = await blobClient.OpenReadAsync();
       
       // Call Google Vision API
       var image = Image.FromStream(stream);
       var request = new AnnotateImageRequest
       {
           Image = image,
           Features = { new Feature { Type = Feature.Types.Type.DocumentTextDetection } }
       };
       
       var response = await _visionClient.AnnotateAsync(request);
       var text = response.TextAnnotations[0].Description;
       var confidence = response.TextAnnotations[0].Confidence;
       
       pages.Add(new OcrPageResult
       {
           PageNumber = pageNum,
           BlobPath = blobPath,
           ExtractedText = text,
           Confidence = confidence
       });
   }
   ```

6. **Combine Text from All Pages**
   ```csharp
   var combinedText = string.Join("\n\n--- PAGE BREAK ---\n\n", 
       pages.Select(p => p.ExtractedText));
   ```

7. **Save Extracted Text**
   ```csharp
   // If text is large (>100KB), save to blob
   if (extractedText.Length > MaxTextLengthForSql)
   {
       var blobPath = await SaveTextToBlobAsync(submissionId, examId, extractedText);
       
       // Save blob reference to database
       await _repository.SaveExtractedTextAsync(
           submissionId, null, null, blobPath, ocrTimeMs);
   }
   else
   {
       // Save directly to database
       await _repository.SaveExtractedTextAsync(
           submissionId, extractedText, extractedTextJson, null, ocrTimeMs);
   }
   ```

8. **Update OCR Completion**
   ```sql
   UPDATE WrittenSubmissions
   SET ExtractedText = @Text,
       ExtractedTextJson = @TextJson,
       ExtractedTextBlobPath = @BlobPath,
       OcrCompletedAt = GETUTCDATE(),
       OcrProcessingTimeMs = @ProcessingTimeMs
   WHERE Id = @SubmissionId
   ```

---

### Phase 4: AI Evaluation

**Function:** `ProcessWrittenSubmission.cs` (continues)  
**Service:** `WrittenAnswerEvaluationService.cs`  
**AI Model:** Azure OpenAI GPT-4

#### Process Flow

1. **Update Status to Evaluating**
   ```sql
   UPDATE WrittenSubmissions
   SET Status = 2, -- Evaluating
       EvaluationStartedAt = GETUTCDATE()
   WHERE Id = @SubmissionId
   ```

2. **Fetch Exam Questions with Rubrics**
   ```sql
   SELECT 
       q.Id, q.QuestionNumber, q.QuestionText, 
       q.ModelAnswer, q.MaxScore, q.Rubric, q.Keywords,
       q.ClassName, q.Subject, q.Chapter
   FROM ExamQuestions q
   WHERE q.ExamId = @ExamId
   ORDER BY q.QuestionNumber
   ```

3. **AI Evaluation per Question**
   ```csharp
   foreach (var question in questions)
   {
       // Construct evaluation prompt
       var prompt = $@"
       You are an expert examiner evaluating student answers.
       
       Question: {question.QuestionText}
       Model Answer: {question.ModelAnswer}
       Marking Rubric: {question.Rubric}
       Max Score: {question.MaxScore}
       Keywords: {string.Join(", ", question.Keywords)}
       
       Student's Answer (from OCR):
       {extractedAnswer}
       
       Evaluate the answer following the rubric. Provide:
       1. Step-wise breakdown of marks
       2. Detailed feedback
       3. Total awarded marks
       ";
       
       // Call Azure OpenAI
       var chatMessages = new[]
       {
           new ChatMessage(ChatRole.System, systemPrompt),
           new ChatMessage(ChatRole.User, prompt)
       };
       
       var response = await _openAiClient.GetChatCompletionsAsync(
           deploymentName: "gpt-4",
           new ChatCompletionsOptions { Messages = chatMessages }
       );
       
       var evaluation = ParseEvaluationResponse(response.Value.Choices[0].Message.Content);
       
       questionEvaluations.Add(new WrittenQuestionEvaluation
       {
           Id = Guid.NewGuid(),
           WrittenSubmissionId = submissionId,
           QuestionId = question.QuestionId,
           QuestionNumber = question.QuestionNumber,
           ExtractedAnswer = extractedAnswer,
           ModelAnswer = question.ModelAnswer,
           MaxScore = question.MaxScore,
           AwardedScore = evaluation.AwardedScore,
           Feedback = evaluation.Feedback,
           RubricBreakdown = evaluation.RubricBreakdown,
           EvaluatedAt = DateTime.UtcNow
       });
   }
   ```

4. **Calculate Overall Results**
   ```csharp
   var totalScore = questionEvaluations.Sum(q => q.AwardedScore);
   var maxPossibleScore = questionEvaluations.Sum(q => q.MaxScore);
   var percentage = (totalScore / maxPossibleScore) * 100;
   var grade = CalculateGrade(percentage);
   
   var evaluationResult = new WrittenEvaluationResult
   {
       WrittenSubmissionId = submissionId,
       ExamId = examId,
       StudentId = studentId,
       TotalScore = totalScore,
       MaxPossibleScore = maxPossibleScore,
       Percentage = percentage,
       Grade = grade,
       QuestionEvaluations = questionEvaluations,
       EvaluatedAt = DateTime.UtcNow
   };
   ```

---

### Phase 5: Result Storage

#### Step 1: Save to Blob Storage (NEW!)

```csharp
private async Task<string> SaveEvaluationResultToBlobAsync(
    Guid submissionId,
    string examId,
    WrittenEvaluationResult result,
    CancellationToken cancellationToken)
{
    // Container and path
    var containerName = "evaluation-results";
    var blobPath = $"{examId}/{submissionId}/evaluation-result.json";
    
    // Create container if not exists
    var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
    await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    
    // Serialize result to JSON
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    var json = JsonSerializer.Serialize(result, jsonOptions);
    
    // Upload to blob
    var blobClient = containerClient.GetBlobClient(blobPath);
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    
    var uploadOptions = new BlobUploadOptions
    {
        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
    };
    
    await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);
    
    return $"{containerName}/{blobPath}";
}
```

#### Step 2: Save to Database

```sql
-- Update WrittenSubmissions
UPDATE WrittenSubmissions
SET Status = 3, -- Completed
    EvaluatedAt = GETUTCDATE(),
    TotalScore = @TotalScore,
    MaxPossibleScore = @MaxPossibleScore,
    Percentage = @Percentage,
    Grade = @Grade,
    EvaluationResultBlobPath = @ResultBlobPath,
    EvaluationProcessingTimeMs = @ProcessingTimeMs
WHERE Id = @SubmissionId;

-- Insert WrittenQuestionEvaluations
INSERT INTO WrittenQuestionEvaluations (
    Id, WrittenSubmissionId, QuestionId, QuestionNumber,
    ExtractedAnswer, ModelAnswer, MaxScore, AwardedScore,
    Feedback, RubricBreakdown, EvaluatedAt
)
VALUES (
    @Id, @SubmissionId, @QuestionId, @QuestionNumber,
    @ExtractedAnswer, @ModelAnswer, @MaxScore, @AwardedScore,
    @Feedback, @RubricBreakdown, GETUTCDATE()
);
```

---

## Status Transitions

```
┌─────────────┐
│   Status 0  │  Uploaded
│  (Uploaded) │  • File uploaded to blob
└──────┬──────┘  • Database record created
       │         • Queue message sent
       │
       ▼ Queue trigger
┌─────────────┐
│   Status 1  │  OcrProcessing
│    (OCR)    │  • Google Vision API called
└──────┬──────┘  • Text extraction in progress
       │         • OcrStartedAt set
       │
       ▼ OCR complete
┌─────────────┐
│   Status 2  │  Evaluating
│  (Evaluate) │  • Azure OpenAI evaluation
└──────┬──────┘  • Question-by-question marking
       │         • EvaluationStartedAt set
       │
       ▼ Evaluation complete
┌─────────────┐
│   Status 3  │  Completed ✅
│ (Completed) │  • Results saved to blob
└─────────────┘  • Database updated
                 • EvaluatedAt set
                 • Student can retrieve results

┌─────────────┐
│   Status 4  │  Failed ❌
│   (Failed)  │  • Error occurred
└─────────────┘  • ErrorMessage populated
                 • RetryCount incremented
```

### Status Code Reference
```csharp
public enum WrittenSubmissionStatus
{
    Uploaded = 0,        // Initial upload complete
    OcrProcessing = 1,   // OCR extraction in progress
    Evaluating = 2,      // AI evaluation in progress
    Completed = 3,       // ✅ Fully processed and evaluated
    Failed = 4           // ❌ Processing failed (see ErrorMessage)
}
```

---

## API Endpoints Reference

### 1. Upload Answer Sheet

```http
POST /api/answers/upload?code={function-key}
Content-Type: multipart/form-data
```

**Request:**
- `examId` (required): Exam identifier
- `studentId` (required): Student identifier
- `files` (required): Image files (.jpg, .jpeg, .png, .pdf)

**Response:**
```json
{
  "submissionId": "uuid",
  "status": 0,
  "message": "Upload successful"
}
```

---

### 2. Get Submission Status

```http
GET /api/submissions/{submissionId}/status?code={function-key}
```

**Response:**
```json
{
  "submissionId": "uuid",
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "status": 3,
  "statusText": "Completed",
  "totalScore": 45.5,
  "maxPossibleScore": 100.0,
  "percentage": 45.5,
  "grade": "F",
  "evaluationResultBlobPath": "evaluation-results/SAMPLE-EXAM-001/uuid/evaluation-result.json",
  "submittedAt": "2025-12-15T10:00:00Z",
  "ocrStartedAt": "2025-12-15T10:00:05Z",
  "ocrCompletedAt": "2025-12-15T10:00:45Z",
  "evaluationStartedAt": "2025-12-15T10:00:46Z",
  "evaluatedAt": "2025-12-15T10:02:30Z"
}
```

---

### 3. Get Evaluation Results (from Database)

```http
GET /api/evaluations/{examId}/{studentId}?code={function-key}
GET /api/evaluations/by-submission/{submissionId}?code={function-key}
```

**Response:**
```json
{
  "submissionId": "uuid",
  "examId": "SAMPLE-EXAM-001",
  "totalScore": 45.5,
  "maxScore": 100.0,
  "percentage": 45.5,
  "grade": "F",
  "evaluatedAt": "2025-12-15T10:02:30Z",
  "questions": [
    {
      "questionNumber": 1,
      "extractedAnswer": "Student's answer text...",
      "awardedScore": 9.5,
      "maxScore": 20.0,
      "feedback": "Good attempt. Missing key details...",
      "rubricBreakdown": "Step 1: 2/3\nStep 2: 2/4\n..."
    }
  ]
}
```

---

### 4. Get Evaluation Result (from Blob) ⭐ NEW

```http
GET /api/evaluations/{submissionId}/result?code={function-key}
```

**Response:**
```json
{
  "writtenSubmissionId": "uuid",
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "totalScore": 45.5,
  "maxPossibleScore": 100.0,
  "percentage": 45.5,
  "grade": "F",
  "evaluatedAt": "2025-12-15T10:02:30Z",
  "questionEvaluations": [
    {
      "id": "eval-uuid",
      "questionId": "q-uuid",
      "questionNumber": 1,
      "extractedAnswer": "Full text...",
      "modelAnswer": "Expected answer...",
      "maxScore": 20.0,
      "awardedScore": 9.5,
      "feedback": "Detailed feedback...",
      "rubricBreakdown": "Complete breakdown...",
      "evaluatedAt": "2025-12-15T10:02:30Z"
    }
  ]
}
```

---

### 5. Get Direct Download URL ⭐ NEW

```http
GET /api/evaluations/{submissionId}/download-url?code={function-key}
```

**Response:**
```json
{
  "submissionId": "uuid",
  "downloadUrl": "https://stsmartstudydev.blob.core.windows.net/evaluation-results/SAMPLE-EXAM-001/uuid/evaluation-result.json?sv=2021-06-08&se=2025-12-15T23:59:59Z&sr=b&sp=r&sig=...",
  "expiresAt": "2025-12-15T23:59:59Z",
  "expiresInHours": 24
}
```

---

### 6. Direct Text Evaluation (No File Upload)

```http
POST /api/answers/evaluate?code={function-key}
Content-Type: application/json
```

**Request:**
```json
{
  "examId": "SAMPLE-EXAM-001",
  "questionId": "q-uuid",
  "studentAnswerText": "Direct text answer without OCR"
}
```

**Response:**
```json
{
  "submissionId": "uuid",
  "questionId": "q-uuid",
  "status": 3,
  "awardedScore": 9.5,
  "maxScore": 20.0,
  "feedback": "...",
  "rubricBreakdown": "...",
  "evaluatedAt": "2025-12-15T10:00:00Z"
}
```

---

## Data Flow

### Database Tables

#### WrittenSubmissions
```sql
CREATE TABLE WrittenSubmissions (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ExamId NVARCHAR(200) NOT NULL,
    StudentId NVARCHAR(200) NOT NULL,
    FilePaths NVARCHAR(MAX) NOT NULL, -- JSON array
    Status INT NOT NULL DEFAULT 0,
    
    -- OCR fields
    ExtractedText NVARCHAR(MAX),
    ExtractedTextJson NVARCHAR(MAX),
    ExtractedTextBlobPath NVARCHAR(500),
    OcrStartedAt DATETIME2,
    OcrCompletedAt DATETIME2,
    OcrProcessingTimeMs BIGINT,
    
    -- Evaluation fields
    TotalScore DECIMAL(10,2),
    MaxPossibleScore DECIMAL(10,2),
    Percentage DECIMAL(5,2),
    Grade NVARCHAR(10),
    EvaluationResultBlobPath NVARCHAR(500), -- ⭐ NEW
    EvaluationStartedAt DATETIME2,
    EvaluatedAt DATETIME2,
    EvaluationProcessingTimeMs BIGINT,
    
    -- Metadata
    SubmittedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    RetryCount INT DEFAULT 0,
    ErrorMessage NVARCHAR(MAX),
    BlobsDeleted BIT DEFAULT 0
);
```

#### WrittenQuestionEvaluations
```sql
CREATE TABLE WrittenQuestionEvaluations (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    WrittenSubmissionId UNIQUEIDENTIFIER NOT NULL,
    QuestionId UNIQUEIDENTIFIER NOT NULL,
    QuestionNumber INT NOT NULL,
    
    ExtractedAnswer NVARCHAR(MAX),
    ModelAnswer NVARCHAR(MAX),
    MaxScore DECIMAL(10,2) NOT NULL,
    AwardedScore DECIMAL(10,2) NOT NULL,
    
    Feedback NVARCHAR(MAX),
    RubricBreakdown NVARCHAR(MAX),
    EvaluatedAt DATETIME2 NOT NULL,
    
    FOREIGN KEY (WrittenSubmissionId) REFERENCES WrittenSubmissions(Id)
);
```

---

## Error Handling

### Retry Logic

```csharp
// ProcessWrittenSubmission has built-in retry
[Function(nameof(ProcessWrittenSubmission))]
[FixedDelayRetry(3, "00:00:10")] // 3 retries, 10 seconds apart
public async Task Run(...)
```

### Poison Queue

Failed messages after max retries are moved to:
```
written-submission-processing-poison
```

### Error Status Flow

```
Status 0/1/2  →  [Error occurs]  →  Status 4 (Failed)
                                     ↓
                               ErrorMessage populated
                               RetryCount incremented
```

### Common Error Scenarios

1. **OCR Failure**
   - Google Vision API timeout
   - Invalid image format
   - Low confidence score

2. **Evaluation Failure**
   - Azure OpenAI rate limit
   - Invalid question format
   - Missing rubric

3. **Blob Storage Failure**
   - Upload timeout
   - Container not found
   - Access denied

---

## Testing Guide

### 1. Test Complete Flow

```powershell
# Step 1: Upload answer sheet
$boundary = [Guid]::NewGuid().ToString()
$body = @"
--$boundary
Content-Disposition: form-data; name="examId"

SAMPLE-EXAM-001
--$boundary
Content-Disposition: form-data; name="studentId"

TEST-STUDENT
--$boundary
Content-Disposition: form-data; name="files"; filename="answer.jpg"
Content-Type: image/jpeg

<binary data>
--$boundary--
"@

$result = Invoke-RestMethod `
    -Uri "https://smartstudy-func.azurewebsites.net/api/answers/upload?code=$KEY" `
    -Method POST `
    -Body $body `
    -ContentType "multipart/form-data; boundary=$boundary"

$submissionId = $result.submissionId
Write-Host "Submission ID: $submissionId"

# Step 2: Poll status (wait for completion)
do {
    Start-Sleep -Seconds 5
    $status = Invoke-RestMethod `
        -Uri "https://smartstudy-func.azurewebsites.net/api/submissions/$submissionId/status?code=$KEY"
    
    Write-Host "Status: $($status.status) - $($status.statusText)"
} while ($status.status -lt 3)

# Step 3: Get results from blob
$result = Invoke-RestMethod `
    -Uri "https://smartstudy-func.azurewebsites.net/api/evaluations/$submissionId/result?code=$KEY"

Write-Host "Total Score: $($result.totalScore)/$($result.maxPossibleScore)"
Write-Host "Grade: $($result.grade)"
```

### 2. Verify Database Updates

```sql
-- Check submission status
SELECT 
    Id,
    Status,
    TotalScore,
    Grade,
    EvaluationResultBlobPath,
    SubmittedAt,
    EvaluatedAt
FROM WrittenSubmissions
WHERE Id = 'submission-uuid';

-- Check question evaluations
SELECT 
    QuestionNumber,
    AwardedScore,
    MaxScore,
    LEFT(Feedback, 100) AS FeedbackPreview
FROM WrittenQuestionEvaluations
WHERE WrittenSubmissionId = 'submission-uuid'
ORDER BY QuestionNumber;
```

### 3. Verify Blob Storage

```powershell
# Check evaluation result exists
az storage blob exists `
    --account-name stsmartstudydev `
    --container-name evaluation-results `
    --name "SAMPLE-EXAM-001/$submissionId/evaluation-result.json" `
    --auth-mode login
```

---

## Performance Metrics

### Average Processing Times
- **Upload**: 1-2 seconds
- **OCR** (per page): 2-4 seconds
- **Evaluation** (per question): 3-5 seconds
- **Total** (3-question exam): 15-25 seconds

### Scalability
- **Concurrent uploads**: Unlimited (blob storage)
- **Concurrent OCR**: Limited by Google Vision API quota
- **Concurrent evaluations**: Limited by Azure OpenAI TPM
- **Queue processing**: Auto-scales with Azure Functions

---

## Summary

This end-to-end flow ensures:
- ✅ **Reliable** - Queue-based async processing with retries
- ✅ **Scalable** - Auto-scaling Azure Functions
- ✅ **Permanent** - Evaluation results stored in blob forever
- ✅ **Fast** - Parallel OCR and evaluation processing
- ✅ **Detailed** - Step-wise marking with feedback
- ✅ **Accessible** - Multiple retrieval options (database, blob, SAS URL)

**Key Innovation:** Permanent blob storage of evaluation results ensures students never lose their detailed evaluation data, even after source files are cleaned up.
