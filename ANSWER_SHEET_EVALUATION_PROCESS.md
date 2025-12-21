# 📋 Answer Sheet Evaluation Process

## Complete Flow of Answer Sheet Evaluation Using Azure Functions

---

## 🎯 Overview

This document explains the complete process of how student answer sheets are evaluated in the SmartStudy system, from upload to final results. The system uses Azure Functions, Azure Storage, OCR services, and AI-powered evaluation.

---

## 🔄 Complete Evaluation Flow

```
┌──────────────────┐
│  Student Mobile  │
│      App         │
└────────┬─────────┘
         │
         │ 1. Upload Answer Sheet (POST)
         ▼
┌──────────────────────────────────────┐
│  UploadAnswer Function               │
│  Route: /api/answers/upload          │
│  ────────────────────────────────    │
│  • Validates file (PDF/JPG/PNG)      │
│  • Uploads to Azure Blob Storage     │
│  • Extracts text using OCR           │
│  • Returns: extractedText & blobPath │
└────────┬─────────────────────────────┘
         │
         │ 2. Extracted Text + Metadata
         ▼
┌──────────────────────────────────────┐
│  EvaluateAnswer Function             │
│  Route: /api/answers/evaluate        │
│  ────────────────────────────────    │
│  • Fetches ideal answer from DB      │
│  • Compares with student answer      │
│  • Uses AI for scoring               │
│  • Generates detailed feedback       │
│  • Saves to EvaluatedAnswers table   │
└────────┬─────────────────────────────┘
         │
         │ 3. Evaluation Results
         ▼
┌──────────────────────────────────────┐
│  GetEvaluationResults Function       │
│  Routes:                             │
│  • GET /evaluations/exam/{examId}    │
│  • GET /evaluations/{id}             │
│  ────────────────────────────────    │
│  • Retrieves stored evaluations      │
│  • Calculates aggregate scores       │
│  • Returns detailed results          │
└────────┬─────────────────────────────┘
         │
         │ 4. Display Results
         ▼
┌──────────────────┐
│  Student Mobile  │
│  Results Screen  │
└──────────────────┘
```

---

## 📝 Detailed Function Breakdown

### **Function 1: UploadAnswer**

**File:** `Functions/UploadAnswer.cs`  
**Route:** `POST /api/answers/upload`  
**Content-Type:** `multipart/form-data`

#### Purpose
Upload student answer sheet image/PDF and extract text using OCR.

#### Request Parameters
```
- examId (required): Exam identifier
- questionId (required): Question number
- file (required): Answer sheet image/PDF
```

#### Process Steps
1. **Validate Request**
   - Check content type is multipart/form-data
   - Validate examId and questionId are provided
   - Verify file is uploaded

2. **File Validation**
   - Check file extension (.pdf, .jpg, .jpeg, .png)
   - Verify file size (max 10MB)
   - Ensure file is not empty

3. **Upload to Blob Storage**
   - Generate unique blob name: `answers/{examId}/{questionId}/{timestamp}{extension}`
   - Create container if not exists: `student-answers`
   - Upload file to Azure Blob Storage

4. **OCR Text Extraction**
   - Uses `OcrService` (Azure Document Intelligence)
   - Extracts text from uploaded image/PDF
   - Handles multiple pages in PDF
   - Returns extracted text

5. **Response**
   ```json
   {
     "success": true,
     "examId": 21,
     "questionId": 105,
     "extractedText": "Matrix is a rectangular array...",
     "extractedLength": 450,
     "blobPath": "answers/21/105/20251216043012_answer.jpg",
     "fileName": "answer.jpg",
     "fileSize": 245678
   }
   ```

#### Services Used
- `OcrService`: Azure Document Intelligence for text extraction
- `BlobServiceClient`: Azure Blob Storage for file storage

---

### **Function 2: EvaluateAnswer**

**File:** `Functions/EvaluateAnswer.cs`  
**Route:** `POST /api/answers/evaluate`  
**Content-Type:** `application/json`

#### Purpose
Evaluate student answer using AI and save results to database.

#### Request Body
```json
{
  "examId": 21,
  "questionId": 105,
  "studentAnswerText": "A matrix is a rectangular array...",
  "extractedText": "...",
  "blobPath": "answers/21/105/..."
}
```

#### Process Steps

1. **Validate Request**
   - Parse JSON request body
   - Validate examId > 0
   - Validate questionId > 0
   - Ensure studentAnswerText is not empty

2. **Fetch Question Data from Database**
   - Query `GeneratedQuestions` table
   - Retrieve:
     - `IdealAnswer`: Expected/model answer
     - `Marks`: Maximum marks for question
     - `Keywords`: Key concepts to check (JSON array)

3. **AI Scoring Process** (Uses `AiScoringService`)
   
   a. **Prepare Evaluation Prompt**
   - Include ideal answer
   - Include student answer
   - Include maximum marks
   - Include keywords to check
   
   b. **Call Azure OpenAI**
   - Model: GPT-4 (configured deployment)
   - Temperature: 0.2 (for consistent evaluation)
   - Response format: JSON
   - System prompt: "Karnataka PUC Mathematics examiner"
   
   c. **Evaluation Criteria (AI checks)**
   - Mathematical correctness (40%)
   - Step-by-step working (30%)
   - Use of correct formulas/theorems (20%)
   - Presentation and notation (10%)
   
   d. **AI Response Structure**
   ```json
   {
     "score": 4.5,
     "feedback": "Excellent understanding...",
     "missingPoints": ["determinant calculation"],
     "strengths": ["Good use of terminology", "Clear examples"],
     "improvement": "Could mention determinant steps"
   }
   ```
   
   e. **Keyword Analysis**
   - Check which keywords appear in student answer
   - Identify matched keywords
   - Identify missing keywords
   
   f. **Retry Logic**
   - Max 3 retries with exponential backoff
   - Handles rate limiting (429 errors)
   - Fallback to keyword-based scoring if AI fails

4. **Save to Database** (`EvaluatedAnswers` table)
   - Insert new evaluation record
   - Store:
     - ExamId, QuestionId
     - StudentAnswer, ExtractedText
     - IdealAnswer
     - Score, MaxMarks
     - Feedback
     - KeywordsMatched (JSON)
     - MissingKeywords (JSON)
     - Strengths (JSON)
     - ImprovementSuggestions
     - BlobPath
     - EvaluatedOn (timestamp)
   - Returns new evaluation ID

5. **Build Response**
   - Calculate percentage: (Score / MaxMarks) * 100
   - Format strengths and improvements
   - Return comprehensive evaluation results

#### Response
```json
{
  "success": true,
  "evaluationId": 42,
  "examId": 21,
  "questionId": 105,
  "score": 4.5,
  "maxMarks": 5,
  "percentage": 90.0,
  "feedback": "Excellent understanding. Clear explanation.",
  "strengths": "Good terminology; included examples",
  "improvements": "Could mention determinant calculation",
  "keywordsMatched": ["matrix", "determinant", "rank"],
  "missingKeywords": ["inverse"],
  "usedFallback": false
}
```

#### Services Used
- `AiScoringService`: Azure OpenAI integration
- `SqlConnection`: Database operations

---

### **Function 3: GetEvaluationResults**

**File:** `Functions/GetEvaluationResults.cs`  
**Routes:**
- `GET /api/evaluations/exam/{examId}`
- `GET /api/evaluations/exam/{examId}/question/{questionId}`
- `GET /api/evaluations/{id}`

#### Purpose
Retrieve evaluation results for display to students.

#### Route 1: Get All Evaluations for Exam
**Endpoint:** `GET /api/evaluations/exam/{examId}`

**Process:**
1. Query `EvaluatedAnswers` table with JOIN to `GeneratedQuestions`
2. Fetch all evaluations for the exam
3. Calculate aggregate statistics:
   - Total questions evaluated
   - Total score obtained
   - Total marks possible
   - Percentage: (Total Score / Total Marks) * 100
4. Order by evaluation date (most recent first)

**Response:**
```json
{
  "success": true,
  "examId": 21,
  "totalQuestions": 10,
  "totalScore": 42.5,
  "totalMarks": 50,
  "percentage": 85.0,
  "evaluations": [
    {
      "id": 42,
      "questionId": 105,
      "questionText": "Explain matrix...",
      "score": 4.5,
      "maxMarks": 5,
      "feedback": "...",
      "keywordsMatched": ["matrix", "determinant"],
      "missingKeywords": ["inverse"],
      "createdOn": "2025-12-16T04:30:00"
    },
    // ... more evaluations
  ]
}
```

#### Route 2: Get Evaluation for Specific Question
**Endpoint:** `GET /api/evaluations/exam/{examId}/question/{questionId}`

**Process:**
1. Query specific evaluation record
2. Include question text, ideal answer
3. Show detailed breakdown

**Response:**
```json
{
  "success": true,
  "evaluation": {
    "id": 42,
    "examId": 21,
    "questionId": 105,
    "questionText": "Explain matrix operations...",
    "idealAnswer": "A matrix is a rectangular array...",
    "studentAnswer": "Matrix is a rectangular array...",
    "score": 4.5,
    "maxMarks": 5,
    "percentage": 90.0,
    "feedback": "Excellent understanding...",
    "strengths": ["Good terminology", "Clear examples"],
    "improvements": "Could mention determinant steps",
    "keywordsMatched": ["matrix", "determinant", "rank"],
    "missingKeywords": ["inverse"],
    "evaluatedOn": "2025-12-16T04:30:00"
  }
}
```

#### Route 3: Get Evaluation by ID
**Endpoint:** `GET /api/evaluations/{id}`

**Process:**
1. Fetch single evaluation by ID
2. Include all details and metadata

---

## 🔄 Alternative: Combined Upload + Evaluation Flow

### **ProcessWrittenSubmission (Queue-Triggered)**

**File:** `Functions/ProcessWrittenSubmission.cs`  
**Trigger:** Azure Queue message

#### Purpose
Process written submissions asynchronously using queue for better scalability.

#### Flow
1. **Queue Message Received**
   - Contains: submissionId, examId, studentId, blobPath

2. **OCR Processing**
   - Downloads image from blob storage
   - Uses Google Vision OCR (alternative to Azure)
   - Extracts text from answer sheet
   - Updates status: `Uploaded` → `OcrProcessing`

3. **Evaluation**
   - Calls evaluation service with extracted text
   - Uses syllabus-based RAG for context
   - Generates detailed feedback
   - Updates status: `OcrProcessing` → `Evaluating` → `Completed`

4. **Error Handling**
   - Max 3 retries with exponential backoff
   - Updates status to `Failed` if all retries fail
   - Logs detailed error information

---

## 📊 Database Schema

### **EvaluatedAnswers Table**
```sql
CREATE TABLE EvaluatedAnswers (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ExamId INT NOT NULL,
    QuestionId INT NOT NULL,
    StudentAnswer NVARCHAR(MAX),
    ExtractedText NVARCHAR(MAX),
    IdealAnswer NVARCHAR(MAX),
    Score DECIMAL(5,2) NOT NULL,
    MaxMarks INT NOT NULL,
    Feedback NVARCHAR(MAX),
    KeywordsMatched NVARCHAR(MAX),  -- JSON array
    MissingKeywords NVARCHAR(MAX),  -- JSON array
    Strengths NVARCHAR(MAX),        -- JSON array
    ImprovementSuggestions NVARCHAR(MAX),
    BlobPath NVARCHAR(500),
    EvaluatedOn DATETIME2 DEFAULT GETUTCDATE(),
    FOREIGN KEY (QuestionId) REFERENCES GeneratedQuestions(Id)
);
```

---

## 🔧 Key Services

### 1. **OcrService** (`Services/OcrService.cs`)
- **Purpose:** Extract text from images/PDFs
- **Technology:** Azure Document Intelligence (Form Recognizer)
- **Features:**
  - Handles multiple file formats
  - Multi-page PDF support
  - High accuracy text extraction
  - Confidence scores for extracted text

### 2. **AiScoringService** (`Services/AiScoringService.cs`)
- **Purpose:** AI-powered answer evaluation
- **Technology:** Azure OpenAI (GPT-4)
- **Features:**
  - Karnataka PUC Mathematics context
  - Step-by-step evaluation
  - Keyword analysis
  - Fallback scoring mechanism
  - Retry logic with exponential backoff

### 3. **GoogleVisionOcrService** (`Services/GoogleVisionOcrService.cs`)
- **Purpose:** Alternative OCR service
- **Technology:** Google Cloud Vision API
- **Features:**
  - High accuracy for handwritten text
  - Better handling of various handwriting styles
  - Used in queue-triggered processing

### 4. **WrittenAnswerEvaluationService** (`Services/WrittenAnswerEvaluationService.cs`)
- **Purpose:** Enhanced evaluation with syllabus context
- **Features:**
  - Syllabus-based evaluation
  - RAG (Retrieval-Augmented Generation)
  - Detailed step-wise marking
  - Expected answer generation

---

## 🎓 Evaluation Criteria

### AI Evaluation Breakdown
The AI evaluates answers based on:

1. **Mathematical Correctness (40%)**
   - Correct formulas and theorems
   - Accurate calculations
   - Proper mathematical notation

2. **Step-by-Step Working (30%)**
   - Clear methodology
   - Logical progression
   - All steps shown

3. **Use of Correct Formulas/Theorems (20%)**
   - Appropriate formula selection
   - Correct application
   - Proper citations

4. **Presentation and Notation (10%)**
   - Clear writing
   - Proper mathematical symbols
   - Well-organized solution

### Keyword Matching
- System checks for key concepts in answer
- Identifies which keywords are present
- Highlights missing important concepts
- Provides targeted improvement suggestions

---

## 💾 Data Flow Example

### Example: Student Answers a 5-Mark Question

**Step 1: Upload**
```
Student uploads answer sheet image (2.3 MB JPG)
↓
Stored at: answers/21/105/20251216043012_answer.jpg
↓
OCR extracts: "A matrix is a rectangular array of numbers..."
```

**Step 2: Fetch Ideal Answer**
```sql
SELECT IdealAnswer, Marks, Keywords 
FROM GeneratedQuestions 
WHERE Id = 105

Returns:
- IdealAnswer: "A matrix is a rectangular array..."
- Marks: 5
- Keywords: ["matrix", "array", "determinant", "rank", "inverse"]
```

**Step 3: AI Evaluation**
```
Azure OpenAI evaluates:
- Student Answer vs Ideal Answer
- Checks keywords: found 3/5 (matrix, array, determinant)
- Missing: rank, inverse
- Generates score: 4.5/5
- Provides feedback
```

**Step 4: Save Results**
```sql
INSERT INTO EvaluatedAnswers 
VALUES (21, 105, "A matrix is...", 4.5, 5, "Excellent...", ...)
```

**Step 5: Return to Student**
```json
{
  "score": 4.5,
  "maxMarks": 5,
  "percentage": 90.0,
  "feedback": "Excellent understanding...",
  "improvements": "Include rank and inverse concepts"
}
```

---

## 🚀 Performance Optimizations

1. **Parallel Processing**
   - Multiple answers can be processed simultaneously
   - Queue-based processing for scalability

2. **Retry Logic**
   - Automatic retries for transient failures
   - Exponential backoff to avoid overwhelming services

3. **Caching**
   - Ideal answers cached in evaluation service
   - Reduces database queries

4. **Batch Evaluation**
   - `BatchEvaluate` function processes multiple answers
   - Controlled concurrency (max 3 concurrent evaluations)

---

## 📱 Mobile App Integration

### Recommended Flow for Mobile Apps

```javascript
// 1. Upload answer sheet
const uploadResponse = await uploadAnswerSheet(examId, questionId, imageFile);

// 2. Automatically evaluate using extracted text
const evaluateResponse = await evaluateAnswer({
  examId: examId,
  questionId: questionId,
  studentAnswerText: uploadResponse.extractedText,
  blobPath: uploadResponse.blobPath
});

// 3. Display results to student
displayResults(evaluateResponse);
```

---

## 🔍 Error Handling

### Common Scenarios

1. **File Upload Fails**
   - Returns 400 with error message
   - Common causes: invalid format, file too large

2. **OCR Extraction Fails**
   - Returns extracted text as empty string
   - Provides error details in response

3. **AI Evaluation Fails**
   - Automatic fallback to keyword-based scoring
   - `usedFallback: true` in response

4. **Database Errors**
   - Returns 500 with error details
   - Logged for troubleshooting

---

## 📈 Monitoring & Logging

### Key Metrics Logged
- Upload duration
- OCR processing time
- AI evaluation time
- Total processing time
- Success/failure rates
- Retry counts

### Log Levels
- **Information:** Normal flow events
- **Warning:** Retry attempts, fallback usage
- **Error:** Failed operations, exceptions

---

## 🛠️ Configuration

### Required Environment Variables
```
SQL_CONNECTION_STRING=<Azure SQL connection string>
AZURE_OPENAI_ENDPOINT=<Azure OpenAI endpoint>
AZURE_OPENAI_KEY=<Azure OpenAI API key>
AZURE_OPENAI_DEPLOYMENT_NAME=<Model deployment name>
AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT=<Document Intelligence endpoint>
AZURE_DOCUMENT_INTELLIGENCE_KEY=<Document Intelligence key>
AzureWebJobsStorage=<Azure Storage connection string>
```

---

## 📝 Summary

The answer sheet evaluation process involves:

1. **Upload** → Student uploads answer sheet image
2. **Storage** → File stored in Azure Blob Storage
3. **OCR** → Text extracted using AI-powered OCR
4. **Evaluation** → AI compares with ideal answer and scores
5. **Feedback** → Detailed feedback generated with improvements
6. **Results** → Stored in database and returned to student

The entire process is **automated**, **scalable**, and provides **instant feedback** to students with detailed insights for improvement.
