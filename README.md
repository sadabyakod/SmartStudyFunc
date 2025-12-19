# SmartStudy - Azure Functions Backend

**State Board Blueprint Step-Wise Answer Evaluation System**

A production-ready Azure Functions backend for automated evaluation of handwritten student answers using OCR, RAG-based syllabus retrieval, and AI-powered step-wise marking following State Board Blueprint rules.

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           SMARTSTUDY ARCHITECTURE                           │
└─────────────────────────────────────────────────────────────────────────────┘

┌──────────────────┐      ┌──────────────────┐      ┌──────────────────┐
│  API App Service │      │  Azure Functions │      │    Azure SQL     │
│  (Thin, Sync)    │      │  (Heavy, Async)  │      │                  │
├──────────────────┤      ├──────────────────┤      ├──────────────────┤
│ • Upload files   │      │ • OCR (Google)   │      │ • UploadedFiles  │
│ • Validate input │ ───► │ • RAG retrieval  │ ───► │ • FileChunks     │
│ • Enqueue jobs   │      │ • AI evaluation  │      │ • Embeddings     │
│ • Return status  │      │ • Step-wise mark │      │ • Submissions    │
└──────────────────┘      └──────────────────┘      └──────────────────┘
         │                         │
         │                         ▼
         │                ┌──────────────────┐
         │                │  Azure Blob      │
         └───────────────►│  Storage         │
                          │ • Textbooks      │
                          │ • Answer Sheets  │
                          └──────────────────┘
```

---

## ✨ Features

### Answer Evaluation (State Board Blueprint Style)
- ✅ **Step-wise marking** with partial credit per step
- ✅ **RAG-based expected answers** from uploaded syllabus
- ✅ **Single OpenAI call per question** (cost-efficient)
- ✅ **Board Blueprint rules enforced**:
  - Correct method + wrong answer → award method marks
  - Correct formula + wrong substitution → partial marks
  - Copied formula without working → 0 for that step
  - Arithmetic error → deduct only calculation marks
  - OCR/spelling errors → NEVER penalize

### Syllabus Processing
- ✅ **Blob-triggered** automatic processing
- ✅ **PDF text extraction** with 3-minute timeout
- ✅ **Semantic chunking** for optimal retrieval
- ✅ **Vector embeddings** via Azure OpenAI
- ✅ **Class/Subject/Chapter** scoped retrieval

### OCR Processing
- ✅ **Google Cloud Vision** for handwritten text
- ✅ **Normalized output** (math symbols, fractions)
- ✅ **Page-wise JSON** storage
- ✅ **Large text blob storage** (>100KB)

### Reliability
- ✅ **Idempotent** processing (no duplicates)
- ✅ **Retry with exponential backoff** (30s, 60s, 120s)
- ✅ **Graceful failure** → 0 marks, never crashes
- ✅ **Application Insights** telemetry

---

## 📁 Project Structure

```
SmartStudyFunc/
├── Functions/
│   ├── ProcessWrittenSubmission.cs   # Queue-triggered answer evaluation
│   ├── HealthCheck.cs                # Health endpoint
│   └── ...
├── Services/
│   ├── WrittenAnswerEvaluationService.cs  # Board blueprint step-wise evaluation
│   ├── SyllabusRagService.cs              # RAG retrieval from syllabus
│   ├── GoogleVisionOcrService.cs          # OCR extraction
│   ├── WrittenSubmissionRepository.cs     # Database operations
│   └── ...
├── Models/
│   ├── WrittenSubmissionModels.cs    # All evaluation models
│   └── ...
├── Utils/
│   ├── Chunker.cs                    # Semantic text chunking
│   ├── EmbeddingMath.cs              # Cosine similarity
│   └── ...
├── sql/
│   ├── 03_CreateWrittenSubmissionsTables.sql
│   ├── 04_WrittenSubmissionsSchema.sql
│   ├── 05_AddSyllabusMetadataToExamQuestions.sql
│   └── ...
├── ProcessBlobFile.cs                # Blob-triggered syllabus processing
├── Program.cs                        # DI configuration
└── host.json                         # Function app settings
```

---

## 🔄 Processing Flows

### 1. Syllabus Upload Flow

```
Upload PDF → Blob Storage → BlobTrigger → Extract Text → Chunk → Embed → SQL
```

| Step | Action |
|------|--------|
| 1 | Upload PDF to `textbooks/{Class}/{Subject}/{Chapter}/file.pdf` |
| 2 | BlobTrigger fires `ProcessBlobFile` |
| 3 | Extract text from PDF |
| 4 | Create semantic chunks |
| 5 | Generate embeddings (Azure OpenAI) |
| 6 | Store in SQL (FileChunks + ChunkEmbeddings) |

### 2. Answer Evaluation Flow

```
Submit → Queue → OCR → RAG → AI Evaluation → SQL
```

| Step | Action |
|------|--------|
| 1 | API enqueues `written-submission-processing` message |
| 2 | Load submission, check idempotency |
| 3 | OCR via Google Cloud Vision |
| 4 | Segment OCR text by question |
| 5 | For each question: RAG → Single OpenAI call → Step-wise evaluation |
| 6 | Save results to SQL |

---

## 📊 Evaluation Output

Each question produces a `StepWiseQuestionEvaluation`:

```json
{
  "questionNumber": 1,
  "maxMarks": 5,
  "expectedAnswer": {
    "summary": "Photosynthesis is the process by which plants...",
    "steps": [
      { "stepNumber": 1, "description": "Define photosynthesis", "marks": 1 },
      { "stepNumber": 2, "description": "Write equation", "marks": 2 },
      { "stepNumber": 3, "description": "Explain process", "marks": 2 }
    ],
    "syllabusChunkIds": [101, 102, 103]
  },
  "studentEvaluation": {
    "steps": [
      { "stepNumber": 1, "awardedMarks": 1, "maxMarks": 1, "reason": "Correct definition" },
      { "stepNumber": 2, "awardedMarks": 1.5, "maxMarks": 2, "reason": "Equation correct, missing CO2" },
      { "stepNumber": 3, "awardedMarks": 2, "maxMarks": 2, "reason": "Good explanation" }
    ],
    "totalAwardedMarks": 4.5,
    "confidenceScore": 0.88
  },
  "overallFeedback": "Good understanding of photosynthesis. Review chemical equation."
}
```

---

## ⚙️ Configuration

### Required Environment Variables

```bash
# Azure Storage
AzureWebJobsStorage=<connection-string>

# Azure SQL
ConnectionStrings__SqlDb=<sql-connection-string>

# Azure OpenAI
AzureOpenAI__Endpoint=https://<resource>.openai.azure.com/
AzureOpenAI__ApiKey=<api-key>
AzureOpenAI__DeploymentName=gpt-4o-mini
AzureOpenAI__EmbeddingDeployment=text-embedding-3-large

# Google Cloud Vision
GOOGLE_APPLICATION_CREDENTIALS=<path-to-credentials.json>
```

### local.settings.json

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "ConnectionStrings:SqlDb": "Server=localhost;Database=SmartStudy;...",
    "AzureOpenAI:Endpoint": "https://...",
    "AzureOpenAI:ApiKey": "...",
    "AzureOpenAI:DeploymentName": "gpt-4o-mini"
  }
}
```

---

## 🗄️ Database Setup

Run migrations in order:

```bash
# 1. Core tables
sqlcmd -S <server> -d SmartStudy -i sql/03_CreateWrittenSubmissionsTables.sql

# 2. Written submissions schema
sqlcmd -S <server> -d SmartStudy -i sql/04_WrittenSubmissionsSchema.sql

# 3. Syllabus metadata for ExamQuestions
sqlcmd -S <server> -d SmartStudy -i sql/05_AddSyllabusMetadataToExamQuestions.sql
```

---

## 🚀 Deployment

### Build

```bash
cd SmartStudyFunc
dotnet build
dotnet publish -c Release -o ./publish
```

### Deploy to Azure

```bash
# Using Azure CLI
az functionapp deployment source config-zip \
  -g <resource-group> \
  -n <function-app-name> \
  --src publish.zip

# Or using VS Code Azure Functions extension
```

### Verify

```bash
# Health check
curl https://<function-app>.azurewebsites.net/api/health

# Expected response
{ "status": "healthy", "timestamp": "2025-12-13T..." }
```

---

## 📈 Monitoring

### Application Insights Queries

```kusto
// Function execution times
requests
| where name == "ProcessWrittenSubmission"
| summarize avg(duration), percentile(duration, 95) by bin(timestamp, 1h)

// Error rates
exceptions
| where timestamp > ago(24h)
| summarize count() by problemId
| top 10 by count_
```

### Key Metrics

| Metric | Target |
|--------|--------|
| OCR processing time | < 30s |
| Evaluation time per question | < 15s |
| Total submission time | < 2 min |
| Error rate | < 1% |

---

## 🔒 Security

- ✅ Strict file validation (type, size, count)
- ✅ Input sanitization
- ✅ No fire-and-forget patterns
- ✅ CancellationToken support
- ✅ Managed identity recommended for production

---

## 📝 Queue Message Format

```json
{
  "writtenSubmissionId": "guid",
  "examId": "EXAM-001",
  "studentId": "STU-001",
  "filePaths": [
    "answer-sheets/EXAM-001/STU-001/page1.jpg",
    "answer-sheets/EXAM-001/STU-001/page2.jpg"
  ],
  "submittedAt": "2025-12-13T10:30:00Z",
  "priority": 1,
  "retryCount": 0
}
```

---

## 📄 License

Proprietary - All rights reserved.

---

## 🆘 Support

For issues or questions, contact the development team.
