# SmartStudy API Documentation

## 📱 Mobile App Integration Guide

**Base URL:** `https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net`

**Authentication:** All API endpoints (except Health Check) require a Function Key in the header:
```
x-functions-key: YOUR_FUNCTION_KEY
```

---

## 🔐 Authentication

Include this header in all API requests:

```http
Content-Type: application/json
x-functions-key: YOUR_FUNCTION_KEY
```

> ⚠️ **Important:** Get your function key from Azure Portal → Function App → App Keys

---

## 📋 API Endpoints

### 1. Health Check ✅

Check if the API is running and connected to all services.

**Endpoint:** `GET /api/health`  
**Auth Required:** No

**Response:**
```json
{
  "status": "running",
  "timestamp": "2025-12-14T15:00:00Z",
  "sql_configured": true,
  "openai_configured": true,
  "database": "connected",
  "response_time_ms": 340
}
```

---

### 2. Evaluate Answer 📝

Evaluate a student's answer against the model answer using AI scoring.

**Endpoint:** `POST /api/answers/evaluate`  
**Auth Required:** Yes

**Request Body:**
```json
{
  "examId": "SAMPLE-EXAM-001",
  "questionId": "2657EA0B-1EA3-4E85-8C37-6710E70D954D",
  "studentAnswerText": "Photosynthesis is the process by which plants convert sunlight into energy...",
  "writtenSubmissionId": "c4a67ea9-73b6-4ac0-9742-65a88ecb9168"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `examId` | string | ✅ | Exam identifier |
| `questionId` | GUID | ✅ | Question UUID from ExamQuestions table |
| `studentAnswerText` | string | ✅ | Student's answer text |
| `writtenSubmissionId` | GUID | ❌ | Optional - Links to WrittenSubmissions for status tracking |

**Success Response (200):**
```json
{
  "success": true,
  "evaluationId": 1,
  "examId": "SAMPLE-EXAM-001",
  "questionId": "2657ea0b-1ea3-4e85-8c37-6710e70d954d",
  "score": 18.9,
  "maxMarks": 20,
  "percentage": 94.5,
  "feedback": "Excellent answer covering all key concepts.",
  "strengths": "Covered 9 key concepts",
  "improvements": "Good coverage of key concepts",
  "keywordsMatched": ["chloroplast", "chlorophyll", "glucose"],
  "missingKeywords": [],
  "usedFallback": false
}
```

**Status Update:** If `writtenSubmissionId` is provided, the `WrittenSubmissions` table is automatically updated:
- `Status` → 3 (Completed)
- `TotalScore` → Accumulated score
- `MaxPossibleScore` → Accumulated max marks
- `Percentage` → Calculated percentage
- `EvaluatedAt` → Current UTC timestamp

---

### 3. Batch Evaluate 📚

Evaluate multiple answers in a single request.

**Endpoint:** `POST /api/answers/evaluate/batch`  
**Auth Required:** Yes

**Request Body:**
```json
{
  "answers": [
    {
      "examId": "SAMPLE-EXAM-001",
      "questionId": "2657EA0B-1EA3-4E85-8C37-6710E70D954D",
      "studentAnswerText": "Answer 1..."
    },
    {
      "examId": "SAMPLE-EXAM-001",
      "questionId": "BF567D89-89E0-44CB-A006-D0F150E7878F",
      "studentAnswerText": "Answer 2..."
    }
  ]
}
```

**Response:**
```json
{
  "success": true,
  "totalEvaluated": 2,
  "totalScore": 35,
  "maxPossibleScore": 40,
  "percentage": 87.5,
  "results": [
    { "questionId": "...", "score": 18, "maxMarks": 20, "feedback": "..." },
    { "questionId": "...", "score": 17, "maxMarks": 20, "feedback": "..." }
  ]
}
```

---

### 4. Get Evaluations by Exam 📊

Retrieve all evaluations for a specific exam.

**Endpoint:** `GET /api/evaluations/exam/{examId}`  
**Auth Required:** Yes

**Example:** `GET /api/evaluations/exam/SAMPLE-EXAM-001`

**Response:**
```json
{
  "examId": "SAMPLE-EXAM-001",
  "totalEvaluations": 3,
  "evaluations": [
    {
      "id": "abc123",
      "questionId": "2657ea0b-...",
      "questionNumber": 1,
      "awardedScore": 18.9,
      "maxScore": 20,
      "feedback": "Excellent answer",
      "evaluatedAt": "2025-12-14T15:21:05Z"
    }
  ]
}
```

---

### 5. Get Evaluation by ID 🔍

Retrieve a specific evaluation by its ID.

**Endpoint:** `GET /api/evaluations/{id}`  
**Auth Required:** Yes

**Example:** `GET /api/evaluations/abc123-def456`

---

### 6. Get Evaluation by Question 🎯

Retrieve evaluation for a specific question in an exam.

**Endpoint:** `GET /api/evaluations/exam/{examId}/question/{questionId}`  
**Auth Required:** Yes

**Example:** `GET /api/evaluations/exam/SAMPLE-EXAM-001/question/2657EA0B-1EA3-4E85-8C37-6710E70D954D`

---

### 7. Upload Answer 📤

Upload a handwritten answer image for OCR processing.

**Endpoint:** `POST /api/answers/upload`  
**Auth Required:** Yes  
**Content-Type:** `multipart/form-data`

**Form Fields:**
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `file` | File | ✅ | Image file (JPG, PNG, PDF) |
| `examId` | string | ✅ | Exam identifier |
| `studentId` | string | ✅ | Student identifier |

**Response:**
```json
{
  "success": true,
  "submissionId": "new-guid-here",
  "status": "Uploaded",
  "message": "Answer uploaded successfully. Processing will begin shortly."
}
```

---

### 8. Generate Questions 🧠

Generate questions from textbook content using AI.

**Endpoint:** `POST /api/questions/generate`  
**Auth Required:** Yes

**Request Body:**
```json
{
  "content": "Chapter content text here...",
  "className": "10th Standard",
  "subject": "Biology",
  "chapter": "Photosynthesis",
  "questionCount": 5,
  "questionTypes": ["short", "long", "mcq"]
}
```

---

### 9. Generate Study Notes 📖

Generate study notes from textbook content.

**Endpoint:** `POST /api/study/notes`  
**Auth Required:** Yes

**Request Body:**
```json
{
  "extractedText": "Chapter content...",
  "chapterName": "Photosynthesis",
  "className": "10th Standard",
  "subject": "Biology"
}
```

---

### 10. Search (RAG Query) 🔎

Search through uploaded textbook content using semantic search.

**Endpoint:** `POST /api/rag/search`  
**Auth Required:** Yes

**Request Body:**
```json
{
  "query": "What is photosynthesis?",
  "topK": 5
}
```

---

### 11. Upload Textbook 📕

Upload a textbook PDF for processing and indexing.

**Endpoint:** `POST /api/upload/textbook`  
**Auth Required:** Yes  
**Content-Type:** `multipart/form-data`

**Form Fields:**
| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `file` | File | ✅ | PDF file |
| `className` | string | ✅ | Class/Grade |
| `subject` | string | ✅ | Subject name |

---

## 📊 Status Codes

| Status | Description |
|--------|-------------|
| 0 | Uploaded - Waiting for processing |
| 1 | OCR Processing - Extracting text |
| 2 | Evaluating - AI scoring in progress |
| 3 | Completed - Evaluation finished |
| 4 | Failed - Error occurred |

---

## 📱 Mobile App Integration Examples

### Swift (iOS)

```swift
import Foundation

let baseURL = "https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net"
let apiKey = "YOUR_FUNCTION_KEY" // Get from Azure Portal → Function App → App Keys

func evaluateAnswer(examId: String, questionId: String, answer: String) async throws -> EvaluationResult {
    let url = URL(string: "\(baseURL)/api/answers/evaluate")!
    var request = URLRequest(url: url)
    request.httpMethod = "POST"
    request.setValue("application/json", forHTTPHeaderField: "Content-Type")
    request.setValue(apiKey, forHTTPHeaderField: "x-functions-key")
    
    let body: [String: Any] = [
        "examId": examId,
        "questionId": questionId,
        "studentAnswerText": answer
    ]
    request.httpBody = try JSONSerialization.data(withJSONObject: body)
    
    let (data, _) = try await URLSession.shared.data(for: request)
    return try JSONDecoder().decode(EvaluationResult.self, from: data)
}
```

### Kotlin (Android)

```kotlin
import retrofit2.http.*

interface SmartStudyApi {
    @POST("api/answers/evaluate")
    suspend fun evaluateAnswer(
        @Header("x-functions-key") apiKey: String,
        @Body request: EvaluateRequest
    ): EvaluationResponse
    
    @GET("api/evaluations/exam/{examId}")
    suspend fun getEvaluations(
        @Header("x-functions-key") apiKey: String,
        @Path("examId") examId: String
    ): EvaluationsResponse
}

data class EvaluateRequest(
    val examId: String,
    val questionId: String,
    val studentAnswerText: String,
    val writtenSubmissionId: String? = null
)
```

### React Native / JavaScript

```javascript
const BASE_URL = 'https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net';
const API_KEY = 'YOUR_FUNCTION_KEY'; // Get from Azure Portal → Function App → App Keys

async function evaluateAnswer(examId, questionId, studentAnswer) {
  const response = await fetch(`${BASE_URL}/api/answers/evaluate`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-functions-key': API_KEY
    },
    body: JSON.stringify({
      examId,
      questionId,
      studentAnswerText: studentAnswer
    })
  });
  
  return await response.json();
}

// Usage
const result = await evaluateAnswer(
  'SAMPLE-EXAM-001',
  '2657EA0B-1EA3-4E85-8C37-6710E70D954D',
  'Photosynthesis is the process by which plants make food using sunlight...'
);

console.log(`Score: ${result.score}/${result.maxMarks} (${result.percentage}%)`);
console.log(`Feedback: ${result.feedback}`);
```

### Flutter / Dart

```dart
import 'dart:convert';
import 'package:http/http.dart' as http;

class SmartStudyApi {
  static const baseUrl = 'https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net';
  static const apiKey = 'YOUR_FUNCTION_KEY'; // Get from Azure Portal → Function App → App Keys
  
  Future<Map<String, dynamic>> evaluateAnswer({
    required String examId,
    required String questionId,
    required String studentAnswer,
  }) async {
    final response = await http.post(
      Uri.parse('$baseUrl/api/answers/evaluate'),
      headers: {
        'Content-Type': 'application/json',
        'x-functions-key': apiKey,
      },
      body: jsonEncode({
        'examId': examId,
        'questionId': questionId,
        'studentAnswerText': studentAnswer,
      }),
    );
    
    return jsonDecode(response.body);
  }
}
```

---

## 🗄️ Database Tables

### ExamQuestions
| Column | Type | Description |
|--------|------|-------------|
| Id | uniqueidentifier | Primary key (GUID) |
| ExamId | nvarchar | Exam identifier |
| QuestionNumber | int | Question number |
| QuestionText | nvarchar | Question text |
| ModelAnswer | nvarchar | Ideal answer |
| MaxScore | decimal | Maximum marks |
| Keywords | nvarchar | JSON array of keywords |

### WrittenSubmissions
| Column | Type | Description |
|--------|------|-------------|
| Id | uniqueidentifier | Primary key (GUID) |
| ExamId | nvarchar | Exam identifier |
| StudentId | nvarchar | Student identifier |
| Status | int | 0=Uploaded, 1=OCR, 2=Evaluating, 3=Completed, 4=Failed |
| TotalScore | decimal | Total awarded score |
| MaxPossibleScore | decimal | Maximum possible score |
| Percentage | decimal | Score percentage |
| EvaluatedAt | datetime2 | Evaluation timestamp |

### WrittenQuestionEvaluations
| Column | Type | Description |
|--------|------|-------------|
| Id | uniqueidentifier | Primary key (GUID) |
| WrittenSubmissionId | uniqueidentifier | FK to WrittenSubmissions |
| QuestionId | uniqueidentifier | FK to ExamQuestions |
| ExtractedAnswer | nvarchar | Student's answer text |
| AwardedScore | decimal | Score given |
| MaxScore | decimal | Maximum possible |
| Feedback | nvarchar | AI feedback |
| RubricBreakdown | nvarchar | JSON with detailed scoring |

---

## ⚠️ Error Handling

**Error Response Format:**
```json
{
  "error": "Error description",
  "details": "Detailed error message"
}
```

**Common Errors:**
| Status Code | Error | Solution |
|-------------|-------|----------|
| 400 | Request body is required | Include JSON body |
| 400 | ExamId is required | Provide examId field |
| 400 | QuestionId is required | Provide valid GUID |
| 401 | Unauthorized | Include x-functions-key header |
| 404 | Question not found | Verify questionId exists |
| 500 | Internal Server Error | Check API logs |

---

## 🔧 Environment Configuration

The API uses these environment variables (already configured in Azure):

| Variable | Description |
|----------|-------------|
| `SQL_CONNECTION_STRING` | Azure SQL Database connection |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint URL |
| `AZURE_OPENAI_KEY` | Azure OpenAI API key |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | GPT model deployment name |
| `AzureWebJobsStorage` | Azure Storage connection |

---

## 📞 Support

- **API Status:** Check `/api/health` endpoint
- **Issues:** Contact development team
- **Documentation:** This README

---

**Last Updated:** December 14, 2025  
**Version:** 1.0.0
