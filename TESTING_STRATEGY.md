# SmartStudy AI - Complete Testing Strategy

## Table of Contents
1. [Test Environment Setup](#test-environment-setup)
2. [Feature Testing](#feature-testing)
3. [Manual Testing Guide](#manual-testing-guide)
4. [Automated Testing](#automated-testing)
5. [SQL Verification Queries](#sql-verification-queries)
6. [Mock Test Data](#mock-test-data)
7. [Edge Cases & Error Scenarios](#edge-cases--error-scenarios)

---

## Test Environment Setup

### Prerequisites
- .NET 8 SDK
- Azure Functions Core Tools v4
- Azurite (Azure Storage Emulator)
- SQL Server (local or Azure)
- Postman or similar HTTP client
- Azure Storage Explorer

### Local Setup Steps

#### 1. Start Azurite
```powershell
# Install Azurite
npm install -g azurite

# Start Azurite
azurite --silent --location c:\azurite --debug c:\azurite\debug.log
```

#### 2. Configure local.settings.json
```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "USE_REAL_EMBEDDINGS": "false",
    "AzureOpenAI:Endpoint": "https://your-resource.openai.azure.com/",
    "AzureOpenAI:ApiKey": "your-api-key",
    "AzureOpenAI:EmbeddingDeployment": "text-embedding-3-small",
    "AzureOpenAI:ChatDeployment": "gpt-4o-mini"
  },
  "ConnectionStrings": {
    "SqlDb": "Server=localhost;Database=SmartStudyTest;Integrated Security=true;TrustServerCertificate=True;"
  }
}
```

#### 3. Create Test Database
```sql
CREATE DATABASE SmartStudyTest;
GO
USE SmartStudyTest;
GO

-- Run all table creation scripts from sql/ folder
```

#### 4. Start Functions App
```powershell
cd C:\SmartStudyFunc\SmartStudyFunc
func start --port 7071
```

---

## Feature Testing

### 1. UploadTextbook.cs

#### Function: POST /api/upload/textbook

**Test Cases:**

| Test Case ID | Description | Priority | Status |
|--------------|-------------|----------|--------|
| UT-001 | Upload valid PDF with all metadata | HIGH | ⬜ |
| UT-002 | Upload without className (should fail) | HIGH | ⬜ |
| UT-003 | Upload without subject (should fail) | HIGH | ⬜ |
| UT-004 | Upload without chapter (should fail) | HIGH | ⬜ |
| UT-005 | Upload non-PDF file (should fail) | HIGH | ⬜ |
| UT-006 | Upload with special characters in metadata | MEDIUM | ⬜ |
| UT-007 | Upload very large PDF (>10MB) | MEDIUM | ⬜ |
| UT-008 | Upload empty file | LOW | ⬜ |
| UT-009 | Upload with duplicate filename | LOW | ⬜ |

**Sample Request (UT-001):**
```bash
POST http://localhost:7071/api/upload/textbook
Content-Type: multipart/form-data

# Form fields:
className: "10"
subject: "Mathematics"
chapter: "Algebra"
file: sample-textbook.pdf
```

**Expected Response (200 OK):**
```json
{
  "success": true,
  "message": "File uploaded successfully",
  "data": {
    "fileName": "sample-textbook.pdf",
    "blobPath": "textbooks/10/Mathematics/Algebra/sample-textbook.pdf",
    "className": "10",
    "subject": "Mathematics",
    "chapter": "Algebra",
    "fileSize": 142786,
    "uploadedAt": "2025-11-24T19:00:00Z"
  }
}
```

**Validation Steps:**
1. ✅ Check HTTP status code = 200
2. ✅ Verify blob exists in Azure Storage Explorer at path: `textbooks/10/Mathematics/Algebra/`
3. ✅ Check blob metadata contains all fields
4. ✅ Verify ProcessBlobFile function triggers automatically
5. ✅ Check logs for "File uploaded successfully"

**Sample Request (UT-002 - Missing className):**
```bash
POST http://localhost:7071/api/upload/textbook
Content-Type: multipart/form-data

# Form fields:
subject: "Mathematics"
chapter: "Algebra"
file: sample-textbook.pdf
```

**Expected Response (400 Bad Request):**
```json
{
  "error": "Missing required fields",
  "message": "className, subject, and chapter are required"
}
```

---

### 2. ProcessBlobFile.cs

#### Function: BlobTrigger on textbooks/{className}/{subject}/{chapter}/{name}

**Test Cases:**

| Test Case ID | Description | Priority | Status |
|--------------|-------------|----------|--------|
| PBF-001 | Process valid PDF textbook | HIGH | ⬜ |
| PBF-002 | Process PDF with multiple pages | HIGH | ⬜ |
| PBF-003 | Process corrupted PDF | MEDIUM | ⬜ |
| PBF-004 | Process empty PDF | MEDIUM | ⬜ |
| PBF-005 | Verify chunking for small text (<1000 chars) | MEDIUM | ⬜ |
| PBF-006 | Verify chunking for large text (>10000 chars) | MEDIUM | ⬜ |
| PBF-007 | Verify embedding generation (fake mode) | HIGH | ⬜ |
| PBF-008 | Verify embedding generation (real Azure OpenAI) | HIGH | ⬜ |
| PBF-009 | Process image-only PDF (OCR pending) | LOW | ⬜ |

**Test Execution:**
1. Upload PDF via UploadTextbook endpoint
2. Wait for BlobTrigger to fire (check logs)
3. Verify processing completes successfully

**Expected Log Output:**
```
[timestamp] ========================================
[timestamp] NEW FILE UPLOADED TO BLOB
[timestamp] Class: 10, Subject: Mathematics, Chapter: Algebra
[timestamp] File: sample-textbook.pdf
[timestamp] ========================================
[timestamp] Processing textbook: Size=142786, Ext=.pdf
[timestamp] Inserted File Metadata ID=1
[timestamp] Extracted 6188 chars
[timestamp] Chunk count: 8
[timestamp] Inserted chunk 1/8 -> ChunkId=1
[timestamp] Inserted chunk 2/8 -> ChunkId=2
...
[timestamp] ========================================
[timestamp] TEXTBOOK PROCESSING COMPLETE → SUCCESS
[timestamp] File: sample-textbook.pdf
[timestamp] Chunks: 8
[timestamp] ========================================
```

**SQL Verification (PBF-001):**
```sql
-- 1. Check UploadedFiles table
SELECT TOP 1 * FROM UploadedFiles 
ORDER BY Id DESC;

-- Expected: 1 row with FileName, FileSizeBytes, ClassName, Subject, Chapter

-- 2. Check FileChunks table
SELECT COUNT(*) AS ChunkCount FROM FileChunks 
WHERE UploadedFileId = (SELECT TOP 1 Id FROM UploadedFiles ORDER BY Id DESC);

-- Expected: 8 chunks

-- 3. Check ChunkEmbeddings table
SELECT COUNT(*) AS EmbeddingCount FROM ChunkEmbeddings ce
INNER JOIN FileChunks fc ON ce.ChunkId = fc.Id
WHERE fc.UploadedFileId = (SELECT TOP 1 Id FROM UploadedFiles ORDER BY Id DESC);

-- Expected: 8 embeddings

-- 4. Verify embedding data
SELECT TOP 1 
    fc.Id AS ChunkId,
    fc.TopicTitle,
    fc.TokenCount,
    LEN(ce.Embedding) AS EmbeddingSize
FROM FileChunks fc
INNER JOIN ChunkEmbeddings ce ON fc.Id = ce.ChunkId
ORDER BY fc.Id DESC;

-- Expected: EmbeddingSize > 0 (should be 12288 bytes for 3072-dimensional vector)
```

**Test with Corrupted PDF (PBF-003):**
```powershell
# Create corrupted PDF
echo "This is not a valid PDF" > corrupted.pdf

# Upload via UploadTextbook
# Expected: ProcessBlobFile should log error and fail gracefully
```

**Expected Error Log:**
```
[timestamp] Error processing file: corrupted.pdf
[timestamp] PDF extraction failed: Invalid PDF format
```

---

### 3. ExtractChapters.cs

#### Function: BlobTrigger on syllabus container (Currently disabled in ProcessBlobFile)

**Test Cases:**

| Test Case ID | Description | Priority | Status |
|--------------|-------------|----------|--------|
| EC-001 | Extract chapters from valid syllabus PDF | HIGH | ⬜ |
| EC-002 | Test OpenAI GPT extraction format | HIGH | ⬜ |
| EC-003 | Handle GPT returning invalid JSON | MEDIUM | ⬜ |
| EC-004 | Test with empty syllabus PDF | MEDIUM | ⬜ |
| EC-005 | Verify SQL inserts into Syllabus table | HIGH | ⬜ |
| EC-006 | Verify SQL inserts into Chapters table | HIGH | ⬜ |

**Note:** This function is currently commented out in ProcessBlobFile.cs. To test:

1. Create `syllabus` container in Azurite
2. Modify ProcessBlobFile.cs to handle syllabus container
3. Upload syllabus PDF

**Sample Syllabus PDF Content:**
```
Karnataka PUC Mathematics Syllabus

UNIT 1: SETS AND RELATIONS
- Chapter 1: Sets
- Chapter 2: Relations and Functions

UNIT 2: ALGEBRA
- Chapter 3: Matrices
- Chapter 4: Determinants

UNIT 3: CALCULUS
- Chapter 5: Limits and Derivatives
- Chapter 6: Integrals
```

**Expected GPT Response:**
```json
[
  {
    "unit": "SETS AND RELATIONS",
    "chapters": ["Sets", "Relations and Functions"]
  },
  {
    "unit": "ALGEBRA",
    "chapters": ["Matrices", "Determinants"]
  },
  {
    "unit": "CALCULUS",
    "chapters": ["Limits and Derivatives", "Integrals"]
  }
]
```

**SQL Verification:**
```sql
-- 1. Check Syllabus table
SELECT * FROM Syllabus ORDER BY Id DESC;

-- Expected: 1 row with FileName and RawText

-- 2. Check Chapters table
SELECT * FROM Chapters 
WHERE SyllabusId = (SELECT TOP 1 Id FROM Syllabus ORDER BY Id DESC);

-- Expected: 6 rows (6 chapters across 3 units)

-- 3. Verify chapter details
SELECT 
    UnitName,
    COUNT(*) AS ChapterCount
FROM Chapters
WHERE SyllabusId = (SELECT TOP 1 Id FROM Syllabus ORDER BY Id DESC)
GROUP BY UnitName;

-- Expected:
-- SETS AND RELATIONS | 2
-- ALGEBRA | 2
-- CALCULUS | 2
```

---

### 4. GenerateQuestions.cs

#### Function: POST /api/questions/generate

**Test Cases:**

| Test Case ID | Description | Priority | Status |
|--------------|-------------|----------|--------|
| GQ-001 | Generate questions for valid chapter | HIGH | ⬜ |
| GQ-002 | Generate questions with invalid chapterId | HIGH | ⬜ |
| GQ-003 | Test with missing chapterId in request | HIGH | ⬜ |
| GQ-004 | Verify GPT returns valid JSON format | HIGH | ⬜ |
| GQ-005 | Handle GPT returning invalid JSON | MEDIUM | ⬜ |
| GQ-006 | Test with slow OpenAI response | MEDIUM | ⬜ |
| GQ-007 | Verify SQL inserts into GeneratedQuestions | HIGH | ⬜ |
| GQ-008 | Test question types distribution (MCQ, True/False, Short) | MEDIUM | ⬜ |

**Sample Request (GQ-001):**
```bash
POST http://localhost:7071/api/questions/generate
Content-Type: application/json

{
  "chapterId": 1,
  "questionCount": 20
}
```

**Expected Response (200 OK):**
```json
{
  "success": true,
  "chapterId": 1,
  "questionsGenerated": 20,
  "questions": [
    {
      "id": 101,
      "questionText": "What is the definition of a set?",
      "questionType": "MCQ",
      "options": ["A collection of distinct objects", "A sequence", "An ordered pair", "A function"],
      "correctAnswer": "A collection of distinct objects",
      "marks": 2,
      "difficulty": "Easy"
    },
    {
      "id": 102,
      "questionText": "The empty set is a subset of every set.",
      "questionType": "True/False",
      "options": ["True", "False"],
      "correctAnswer": "True",
      "marks": 1,
      "difficulty": "Easy"
    }
  ]
}
```

**Sample Request (GQ-002 - Invalid chapterId):**
```bash
POST http://localhost:7071/api/questions/generate
Content-Type: application/json

{
  "chapterId": 99999,
  "questionCount": 20
}
```

**Expected Response (404 Not Found):**
```json
{
  "error": "Chapter not found",
  "message": "Chapter with ID 99999 does not exist"
}
```

**SQL Verification:**
```sql
-- 1. Check GeneratedQuestions table
SELECT COUNT(*) AS QuestionCount FROM GeneratedQuestions
WHERE ChapterId = 1;

-- Expected: 20 questions

-- 2. Verify question types distribution
SELECT 
    QuestionType,
    COUNT(*) AS Count
FROM GeneratedQuestions
WHERE ChapterId = 1
GROUP BY QuestionType;

-- Expected mix of MCQ, True/False, Short Answer

-- 3. Check question details
SELECT TOP 5 
    Id,
    QuestionText,
    QuestionType,
    CorrectAnswer,
    Marks,
    Difficulty
FROM GeneratedQuestions
WHERE ChapterId = 1
ORDER BY Id DESC;

-- Verify all fields are populated correctly
```

---

### 5. GenerateModelExam.cs

#### Function: POST /api/exam/generate

**Test Cases:**

| Test Case ID | Description | Priority | Status |
|--------------|-------------|----------|--------|
| GME-001 | Generate exam with valid parameters | HIGH | ⬜ |
| GME-002 | Generate exam with invalid chapter IDs | HIGH | ⬜ |
| GME-003 | Test with insufficient questions in pool | MEDIUM | ⬜ |
| GME-004 | Verify Part-A/B/C/D distribution | HIGH | ⬜ |
| GME-005 | Test random selection logic (run multiple times) | MEDIUM | ⬜ |
| GME-006 | Verify total marks calculation | HIGH | ⬜ |
| GME-007 | Test with custom marks per section | MEDIUM | ⬜ |
| GME-008 | Verify SQL inserts into GeneratedExams | HIGH | ⬜ |
| GME-009 | Verify SQL inserts into GeneratedExamQuestions | HIGH | ⬜ |

**Sample Request (GME-001):**
```bash
POST http://localhost:7071/api/exam/generate
Content-Type: application/json

{
  "chapterIds": [1, 2, 3],
  "examTitle": "Unit Test - Sets and Relations",
  "totalMarks": 80,
  "partAMarks": 20,
  "partBMarks": 20,
  "partCMarks": 20,
  "partDMarks": 20
}
```

**Expected Response (200 OK):**
```json
{
  "success": true,
  "examId": "uuid-generated-id",
  "examTitle": "Unit Test - Sets and Relations",
  "totalMarks": 80,
  "partA": [
    {
      "questionId": 101,
      "questionText": "What is a set?",
      "questionType": "MCQ",
      "options": ["...", "...", "...", "..."],
      "marks": 2
    }
  ],
  "partB": [
    {
      "questionId": 105,
      "questionText": "Explain the concept of subsets.",
      "questionType": "Short Answer",
      "marks": 5
    }
  ],
  "partC": [
    {
      "questionId": 110,
      "questionText": "Prove that the union of two sets is commutative.",
      "questionType": "Long Answer",
      "marks": 10
    }
  ],
  "partD": [
    {
      "questionId": 115,
      "questionText": "Solve the given problem on set operations.",
      "questionType": "Problem",
      "marks": 15
    }
  ]
}
```

**SQL Verification:**
```sql
-- 1. Check GeneratedExams table
SELECT TOP 1 * FROM GeneratedExams
ORDER BY CreatedOn DESC;

-- Expected: 1 exam with ExamId, ExamTitle, TotalMarks

-- 2. Check GeneratedExamQuestions table
SELECT COUNT(*) AS QuestionCount FROM GeneratedExamQuestions
WHERE ExamId = (SELECT TOP 1 ExamId FROM GeneratedExams ORDER BY CreatedOn DESC);

-- Expected: Multiple questions (depends on exam structure)

-- 3. Verify marks distribution
SELECT 
    Part,
    SUM(Marks) AS TotalMarks,
    COUNT(*) AS QuestionCount
FROM GeneratedExamQuestions
WHERE ExamId = (SELECT TOP 1 ExamId FROM GeneratedExams ORDER BY CreatedOn DESC)
GROUP BY Part;

-- Expected:
-- Part A | 20 | 10
-- Part B | 20 | 4
-- Part C | 20 | 2
-- Part D | 20 | 2

-- 4. Verify question selection from pool
SELECT geq.*, gq.QuestionText, gq.QuestionType
FROM GeneratedExamQuestions geq
INNER JOIN GeneratedQuestions gq ON geq.QuestionId = gq.Id
WHERE ExamId = (SELECT TOP 1 ExamId FROM GeneratedExams ORDER BY CreatedOn DESC)
ORDER BY Part, geq.Id;
```

---

## Manual Testing Guide

### Postman Collection Setup

#### 1. Create Environment Variables
```
baseUrl: http://localhost:7071
```

#### 2. Test Sequence

**Step 1: Upload Textbook**
```
POST {{baseUrl}}/api/upload/textbook
Body: form-data
- className: 10
- subject: Mathematics
- chapter: Sets
- file: [Select test-sets.pdf]
```

**Step 2: Wait for Processing (check logs)**
```
Monitor console for:
- "NEW FILE UPLOADED TO BLOB"
- "TEXTBOOK PROCESSING COMPLETE → SUCCESS"
```

**Step 3: Verify Data in SQL**
```sql
SELECT * FROM UploadedFiles ORDER BY Id DESC;
SELECT * FROM FileChunks ORDER BY Id DESC;
SELECT * FROM ChunkEmbeddings ORDER BY Id DESC;
```

**Step 4: Test RAG Search**
```
POST {{baseUrl}}/api/rag/search
Content-Type: application/json

{
  "question": "What is a set?"
}
```

**Step 5: Generate Study Notes**
```
POST {{baseUrl}}/api/study/notes
Content-Type: application/json

{
  "topic": "Sets and Relations",
  "format": "bullet-points"
}
```

---

## Automated Testing

### Unit Test Example (xUnit)

```csharp
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using System.IO;
using System.Threading.Tasks;

namespace SmartStudyFunc.Tests
{
    public class UploadTextbookTests
    {
        private readonly Mock<ILogger<UploadTextbook>> _loggerMock;
        private readonly Mock<BlobServiceClient> _blobClientMock;

        public UploadTextbookTests()
        {
            _loggerMock = new Mock<ILogger<UploadTextbook>>();
            _blobClientMock = new Mock<BlobServiceClient>();
        }

        [Fact]
        public async Task UploadTextbook_ValidPDF_ReturnsSuccess()
        {
            // Arrange
            var function = new UploadTextbook(_loggerMock.Object, _blobClientMock.Object);
            var request = CreateMockHttpRequest("10", "Math", "Algebra", "test.pdf");

            // Act
            var response = await function.Run(request);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task UploadTextbook_MissingClassName_ReturnsBadRequest()
        {
            // Arrange
            var function = new UploadTextbook(_loggerMock.Object, _blobClientMock.Object);
            var request = CreateMockHttpRequest(null, "Math", "Algebra", "test.pdf");

            // Act
            var response = await function.Run(request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UploadTextbook_NonPDFFile_ReturnsBadRequest()
        {
            // Arrange
            var function = new UploadTextbook(_loggerMock.Object, _blobClientMock.Object);
            var request = CreateMockHttpRequest("10", "Math", "Algebra", "test.txt");

            // Act
            var response = await function.Run(request);

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var content = await response.ReadAsStringAsync();
            Assert.Contains("Only PDF files are allowed", content);
        }

        private HttpRequestData CreateMockHttpRequest(
            string className, string subject, string chapter, string fileName)
        {
            // Implementation details...
        }
    }
}
```

### Integration Test Example

```csharp
public class ProcessBlobFileIntegrationTests : IAsyncLifetime
{
    private readonly TestContext _testContext;

    public ProcessBlobFileIntegrationTests()
    {
        _testContext = new TestContext();
    }

    [Fact]
    public async Task ProcessBlobFile_ValidPDF_CreatesChunksAndEmbeddings()
    {
        // Arrange
        var pdfBytes = File.ReadAllBytes("TestData/sample.pdf");
        var blobName = $"textbooks/10/Math/Algebra/{Guid.NewGuid()}.pdf";
        
        // Act
        await _testContext.UploadToBlob(blobName, pdfBytes);
        await Task.Delay(5000); // Wait for trigger

        // Assert
        var fileRecord = await _testContext.GetLatestUploadedFile();
        Assert.NotNull(fileRecord);
        
        var chunks = await _testContext.GetChunksForFile(fileRecord.Id);
        Assert.NotEmpty(chunks);
        
        var embeddings = await _testContext.GetEmbeddingsForFile(fileRecord.Id);
        Assert.Equal(chunks.Count, embeddings.Count);
    }

    public async Task InitializeAsync()
    {
        await _testContext.InitializeDatabase();
    }

    public async Task DisposeAsync()
    {
        await _testContext.CleanupDatabase();
    }
}
```

---

## SQL Verification Queries

### Complete Data Verification Script

```sql
-- ============================================
-- SmartStudy AI - Data Verification Queries
-- ============================================

-- 1. Check latest uploaded file
SELECT TOP 1 
    Id,
    FileName,
    FileSizeBytes,
    ClassName,
    Subject,
    Chapter,
    UploadedOn
FROM UploadedFiles
ORDER BY Id DESC;

-- 2. Count chunks per file
SELECT 
    uf.FileName,
    COUNT(fc.Id) AS ChunkCount,
    SUM(fc.TokenCount) AS TotalTokens
FROM UploadedFiles uf
LEFT JOIN FileChunks fc ON uf.Id = fc.UploadedFileId
GROUP BY uf.Id, uf.FileName
ORDER BY uf.Id DESC;

-- 3. Verify embeddings
SELECT 
    uf.FileName,
    fc.TopicTitle,
    fc.TokenCount,
    CASE 
        WHEN ce.Embedding IS NOT NULL THEN 'Yes'
        ELSE 'No'
    END AS HasEmbedding,
    LEN(ce.Embedding) AS EmbeddingSize
FROM UploadedFiles uf
INNER JOIN FileChunks fc ON uf.Id = fc.UploadedFileId
LEFT JOIN ChunkEmbeddings ce ON fc.Id = ce.ChunkId
WHERE uf.Id = (SELECT TOP 1 Id FROM UploadedFiles ORDER BY Id DESC)
ORDER BY fc.Id;

-- 4. Check RAG search logs
SELECT TOP 10
    Question,
    LEFT(Answer, 100) + '...' AS Answer,
    ConfidenceScore,
    CreatedOn
FROM RAGSearchLogs
ORDER BY Id DESC;

-- 5. Check chat history
SELECT 
    ConversationId,
    Role,
    LEFT(Message, 100) + '...' AS Message,
    Confidence,
    CreatedOn
FROM ChatHistory
ORDER BY CreatedOn DESC;

-- 6. Verify syllabus and chapters (if implemented)
SELECT 
    s.FileName AS SyllabusFile,
    c.UnitName,
    c.ChapterName
FROM Syllabus s
LEFT JOIN Chapters c ON s.Id = c.SyllabusId
ORDER BY s.Id, c.UnitName, c.ChapterName;

-- 7. Check generated questions (if implemented)
SELECT 
    ChapterId,
    QuestionType,
    COUNT(*) AS QuestionCount
FROM GeneratedQuestions
GROUP BY ChapterId, QuestionType
ORDER BY ChapterId, QuestionType;

-- 8. Check generated exams (if implemented)
SELECT 
    ExamId,
    ExamTitle,
    TotalMarks,
    CreatedOn,
    (SELECT COUNT(*) FROM GeneratedExamQuestions WHERE ExamId = ge.ExamId) AS QuestionCount
FROM GeneratedExams ge
ORDER BY CreatedOn DESC;

-- 9. Data cleanup queries (for testing)
-- WARNING: These will delete data!
/*
DELETE FROM ChunkEmbeddings;
DELETE FROM FileChunks;
DELETE FROM UploadedFiles;
DELETE FROM RAGSearchLogs;
DELETE FROM ChatHistory;
DELETE FROM Chapters;
DELETE FROM Syllabus;
DELETE FROM GeneratedExamQuestions;
DELETE FROM GeneratedExams;
DELETE FROM GeneratedQuestions;

-- Reset identity seeds
DBCC CHECKIDENT ('UploadedFiles', RESEED, 0);
DBCC CHECKIDENT ('FileChunks', RESEED, 0);
DBCC CHECKIDENT ('ChunkEmbeddings', RESEED, 0);
*/
```

---

## Mock Test Data

### 1. Test PDF Files

Create these test files in `TestData/` folder:

**small-valid.pdf** (1-2 pages)
```
Content: Simple text about mathematics basics
Size: ~10KB
Purpose: Test basic PDF processing
```

**medium-textbook.pdf** (5-10 pages)
```
Content: Chapter on Sets and Relations
Size: ~150KB
Purpose: Test chunking and embeddings
```

**large-textbook.pdf** (50+ pages)
```
Content: Complete textbook
Size: ~5MB
Purpose: Test performance and large file handling
```

**corrupted.pdf**
```
Content: Invalid PDF format
Purpose: Test error handling
```

**empty.pdf**
```
Content: Valid PDF with no text
Purpose: Test edge case
```

**image-only.pdf**
```
Content: PDF with only images
Purpose: Test OCR requirements
```

### 2. Sample Blob Paths

```
# Textbook uploads
textbooks/10/Mathematics/Algebra/intro-to-sets.pdf
textbooks/10/Mathematics/Algebra/set-operations.pdf
textbooks/10/Science/Physics/motion-and-force.pdf
textbooks/12/Chemistry/OrganicChemistry/hydrocarbons.pdf

# Syllabus uploads (if implemented)
syllabus/Karnataka-PUC-Math-2025.pdf
syllabus/CBSE-Class10-Science-2025.pdf
```

### 3. Sample SQL Test Data

```sql
-- Insert test uploaded file
INSERT INTO UploadedFiles (FileName, FileSizeBytes, FileType, ClassName, Subject, Chapter, UploadedOn)
VALUES ('test-sets.pdf', 142786, '.pdf', '10', 'Mathematics', 'Sets', GETUTCDATE());

-- Insert test chunks
DECLARE @fileId INT = SCOPE_IDENTITY();

INSERT INTO FileChunks (UploadedFileId, TopicTitle, Summary, ChunkText, TokenCount, PageFrom, PageTo, ChunkType, CreatedOn)
VALUES 
(@fileId, 'Introduction to Sets', 'A set is a collection...', 'Full chunk text here...', 250, 1, 1, 'text', GETUTCDATE()),
(@fileId, 'Set Operations', 'Union and intersection...', 'Full chunk text here...', 300, 2, 2, 'text', GETUTCDATE());

-- Insert test embeddings (fake data)
INSERT INTO ChunkEmbeddings (ChunkId, Embedding, CreatedOn)
SELECT Id, CONVERT(VARBINARY(MAX), REPLICATE(CAST(0x01 AS VARBINARY(1)), 12288)), GETUTCDATE()
FROM FileChunks WHERE UploadedFileId = @fileId;
```

---

## Edge Cases & Error Scenarios

### 1. UploadTextbook Edge Cases

| Scenario | Test Method | Expected Behavior |
|----------|-------------|-------------------|
| Empty filename | Send file with empty name | Return 400 Bad Request |
| Filename with spaces | Upload "my file.pdf" | Should sanitize to "my-file.pdf" |
| Special characters | Upload "file<>?*.pdf" | Should sanitize invalid chars |
| Very long filename | 500+ character filename | Should truncate or sanitize |
| Concurrent uploads | Upload same file simultaneously | Both should succeed with unique IDs |

### 2. ProcessBlobFile Edge Cases

| Scenario | Test Method | Expected Behavior |
|----------|-------------|-------------------|
| PDF extraction fails | Upload corrupted PDF | Log error, skip processing |
| Empty PDF content | Upload blank PDF | Create 0 or minimal chunks |
| Very large PDF (100MB+) | Upload huge file | May timeout, handle gracefully |
| Non-Latin characters | PDF with Chinese/Arabic text | Extract and process correctly |
| Slow OpenAI response | Mock slow API | Implement timeout handling |
| OpenAI rate limit | Send many requests quickly | Retry with backoff |

### 3. ExtractChapters Edge Cases

| Scenario | Test Method | Expected Behavior |
|----------|-------------|-------------------|
| Invalid JSON from GPT | Mock bad response | Log error, retry or fail gracefully |
| Empty syllabus | Upload blank syllabus | Return empty chapter list |
| Unstructured syllabus | Random text format | GPT best effort or fail |
| Missing unit names | Chapters without units | Handle gracefully |

### 4. GenerateQuestions Edge Cases

| Scenario | Test Method | Expected Behavior |
|----------|-------------|-------------------|
| Invalid chapter ID | Send chapterId = -1 | Return 404 Not Found |
| No content for chapter | Empty chapter in DB | Return error or minimal questions |
| Requested more questions than possible | questionCount = 1000 | Return max available |
| GPT returns malformed JSON | Mock invalid response | Retry or return error |
| Database connection failure | Stop SQL Server | Return 500 with error message |

### 5. GenerateModelExam Edge Cases

| Scenario | Test Method | Expected Behavior |
|----------|-------------|-------------------|
| Insufficient questions | Request exam with limited pool | Return error or partial exam |
| Invalid total marks | totalMarks = 0 or negative | Return 400 Bad Request |
| Mismatched part marks | Part A+B+C+D ≠ total | Return validation error |
| Empty chapter list | chapterIds = [] | Return 400 Bad Request |
| Duplicate chapter IDs | chapterIds = [1, 1, 1] | Remove duplicates or error |

---

## Performance Testing

### Load Test Scenarios

```powershell
# Test concurrent uploads (using Apache Bench or similar)
ab -n 100 -c 10 -p testfile.json -T application/json http://localhost:7071/api/upload/textbook

# Monitor metrics:
# - Average response time
# - Success rate
# - Memory usage
# - CPU usage
```

### Performance Benchmarks

| Operation | Expected Time | Max Acceptable |
|-----------|---------------|----------------|
| Upload 1MB PDF | < 2 seconds | 5 seconds |
| Process 10-page PDF | < 10 seconds | 30 seconds |
| Generate embeddings (1 chunk) | < 1 second | 3 seconds |
| RAG search query | < 3 seconds | 10 seconds |
| Generate 20 questions | < 30 seconds | 60 seconds |
| Create model exam | < 10 seconds | 20 seconds |

---

## Logging & Debugging

### Key Log Messages to Monitor

```
✅ Success Logs:
- "File uploaded successfully"
- "TEXTBOOK PROCESSING COMPLETE → SUCCESS"
- "Inserted chunk X/Y"
- "Created embedding for question"

⚠️ Warning Logs:
- "Failed to save user message to chat history"
- "No chunks found in database"

❌ Error Logs:
- "PDF extraction failed"
- "Failed to create embedding"
- "InsertUploadedFile failed"
- "Database connection error"
```

### Debug Configuration

```json
// host.json
{
  "version": "2.0",
  "logging": {
    "logLevel": {
      "default": "Information",
      "Host": "Information",
      "Function": "Information",
      "Host.Aggregator": "Information"
    }
  }
}
```

---

## Test Execution Checklist

### Pre-Test Setup
- [ ] Azurite running
- [ ] SQL Server accessible
- [ ] Test database created and tables exist
- [ ] local.settings.json configured
- [ ] Azure Functions app running (`func start`)
- [ ] Test PDF files prepared
- [ ] Postman collection imported

### During Testing
- [ ] Monitor function logs in real-time
- [ ] Check Azure Storage Explorer for blob uploads
- [ ] Run SQL queries to verify data
- [ ] Test both success and failure scenarios
- [ ] Record response times
- [ ] Check error handling

### Post-Test
- [ ] Review all test results
- [ ] Document any issues found
- [ ] Clean up test data
- [ ] Update test cases based on findings
- [ ] Create bug reports for failures

---

## Continuous Integration (CI/CD)

### GitHub Actions Example

```yaml
name: Azure Functions CI/CD

on:
  push:
    branches: [ master ]
  pull_request:
    branches: [ master ]

jobs:
  build-and-test:
    runs-on: windows-latest
    
    steps:
    - uses: actions/checkout@v2
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore
    
    - name: Run Unit Tests
      run: dotnet test --no-build --verbosity normal
    
    - name: Start Azurite
      run: |
        npm install -g azurite
        Start-Process azurite -WindowStyle Hidden
    
    - name: Run Integration Tests
      run: dotnet test --filter Category=Integration
      env:
        AzureWebJobsStorage: UseDevelopmentStorage=true
```

---

## Test Results Template

| Test ID | Description | Status | Notes | Date |
|---------|-------------|--------|-------|------|
| UT-001 | Upload valid PDF | ✅ PASS | | 2025-11-24 |
| UT-002 | Missing className | ✅ PASS | | 2025-11-24 |
| PBF-001 | Process valid PDF | ⚠️ WARN | Slow performance | 2025-11-24 |
| EC-001 | Extract chapters | ❌ FAIL | GPT timeout | 2025-11-24 |

---

## Contact & Support

For issues or questions:
- Check logs: `C:\SmartStudyFunc\SmartStudyFunc\bin\output\*.log`
- Review Azure Portal for errors
- Check this document for troubleshooting steps

**End of Testing Strategy Document**
