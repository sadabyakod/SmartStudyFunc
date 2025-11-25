# SmartStudy AI - Evaluation Pipeline Setup Guide

## 🎉 Implementation Complete!

All components of the AI-powered student answer evaluation system have been successfully implemented and compiled with **0 errors**!

---

## 📦 What Was Installed

### NuGet Packages Added
```xml
<PackageReference Include="Azure.AI.FormRecognizer" Version="4.1.0" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
<PackageReference Include="Dapper" Version="2.1.35" />
<PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" Version="2.2.0" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Core" Version="2.2.5" />
```

---

## 🗂️ Files Created

### SQL Schema
- `SQL/CreateEvaluatedAnswersTable.sql` - Database table for storing evaluation results

### Azure Functions
- `Functions/UploadAnswer.cs` - Upload answer images/PDFs with OCR extraction
- `Functions/EvaluateAnswer.cs` - AI-powered answer evaluation
- `Functions/GetEvaluationResults.cs` - Retrieve evaluation results (3 endpoints)

### Services
- `Services/OcrService.cs` - Azure Document Intelligence OCR extraction
- `Services/AiScoringService.cs` - Azure OpenAI answer evaluation logic

### SqlDb Updates
- Added `QueryAsync<T>`, `QuerySingleAsync<T>`, `ExecuteScalarAsync<T>` methods with retry logic

### Documentation
- `README_EVALUATION_PIPELINE.md` - Complete implementation guide

---

## ⚙️ Configuration Required

### 1. Azure Resources Setup

#### Create Azure Document Intelligence (Form Recognizer)
```powershell
# Create resource
az cognitiveservices account create `
  --name smartstudy-formrecognizer `
  --resource-group SmartStudyAI `
  --kind FormRecognizer `
  --sku S0 `
  --location eastus

# Get endpoint
az cognitiveservices account show `
  --name smartstudy-formrecognizer `
  --resource-group SmartStudyAI `
  --query "properties.endpoint" --output tsv

# Get key
az cognitiveservices account keys list `
  --name smartstudy-formrecognizer `
  --resource-group SmartStudyAI `
  --query "key1" --output tsv
```

### 2. Update local.settings.json

Add these new environment variables:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "your-existing-storage-connection-string",
    "SqlConnectionString": "your-existing-sql-connection-string",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    
    "AZURE_OPENAI_ENDPOINT": "https://smartstudyai.openai.azure.com/",
    "AZURE_OPENAI_KEY": "your-openai-key",
    "AZURE_OPENAI_DEPLOYMENT_NAME": "gpt-4o-mini",
    
    "AZURE_FORM_RECOGNIZER_ENDPOINT": "https://smartstudy-formrecognizer.cognitiveservices.azure.com/",
    "AZURE_FORM_RECOGNIZER_KEY": "your-form-recognizer-key"
  }
}
```

### 3. Run SQL Migration

Connect to your Azure SQL database and execute:

```sql
-- Switch to your database
USE [school-chatbot-sql-10271900]
GO

-- Run the SQL script
-- Execute: SQL/CreateEvaluatedAnswersTable.sql
```

Or run via command line:

```powershell
sqlcmd -S school-chatbot-sql-10271900.database.windows.net `
  -d school-chatbot-sql-10271900 `
  -U your-username `
  -P your-password `
  -i "c:\SmartStudyFunc\SmartStudyFunc\SQL\CreateEvaluatedAnswersTable.sql"
```

---

## 🧪 Testing the Functions

### Start the Function App

```powershell
cd c:\SmartStudyFunc\SmartStudyFunc
func start
```

### Test 1: Upload Answer Image (OCR)

```powershell
# Prepare a test image (answer sheet photo)
$testImage = "C:\path\to\answer-sheet.jpg"

# Upload and extract text
curl -X POST "http://localhost:7071/api/answers/upload" `
  -F "file=@$testImage" `
  -F "examId=1" `
  -F "questionId=1"
```

**Expected Response:**
```json
{
  "success": true,
  "examId": 1,
  "questionId": 1,
  "extractedText": "A matrix is a rectangular array of numbers...",
  "extractedLength": 450,
  "blobPath": "answers/1/1/20251125_answer.jpg",
  "fileName": "answer.jpg"
}
```

### Test 2: Evaluate Answer (AI Scoring)

```powershell
# Evaluate the extracted text
$body = @{
    examId = 1
    questionId = 1
    studentAnswerText = "A matrix is a rectangular array of numbers arranged in rows and columns. It is used in linear algebra for transformations."
} | ConvertTo-Json

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:7071/api/answers/evaluate" `
  -Body $body `
  -ContentType "application/json"
```

**Expected Response:**
```json
{
  "success": true,
  "evaluationId": 1,
  "examId": 1,
  "questionId": 1,
  "score": 4.5,
  "maxMarks": 5,
  "percentage": 90.0,
  "feedback": "Excellent understanding demonstrated. Clear definition with proper terminology.",
  "strengths": "Good use of technical terms, mentioned key applications",
  "improvements": "Could elaborate on matrix operations like determinant or inverse",
  "keywordsMatched": ["matrix", "array", "rows", "columns"],
  "missingKeywords": ["determinant", "inverse"]
}
```

### Test 3: Get Evaluation Results

```powershell
# Get all evaluations for an exam
Invoke-RestMethod -Method Get `
  -Uri "http://localhost:7071/api/evaluations/exam/1"

# Get specific evaluation
Invoke-RestMethod -Method Get `
  -Uri "http://localhost:7071/api/evaluations/exam/1/question/1"

# Get by evaluation ID
Invoke-RestMethod -Method Get `
  -Uri "http://localhost:7071/api/evaluations/1"
```

---

## 🔍 Verify Database Tables

After running the SQL migration, verify the table was created:

```sql
-- Check table exists
SELECT * FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_NAME = 'EvaluatedAnswers'

-- Check table structure
EXEC sp_help 'EvaluatedAnswers'

-- Check foreign keys
SELECT 
    fk.name AS ForeignKeyName,
    tp.name AS ParentTable,
    tr.name AS ReferencedTable
FROM sys.foreign_keys AS fk
INNER JOIN sys.tables AS tp ON fk.parent_object_id = tp.object_id
INNER JOIN sys.tables AS tr ON fk.referenced_object_id = tr.object_id
WHERE tp.name = 'EvaluatedAnswers'
```

---

## 🚀 End-to-End Test Workflow

### Complete Student Answer Evaluation Flow

```powershell
# Step 1: Upload answer image
$uploadResponse = Invoke-RestMethod -Method Post `
  -Uri "http://localhost:7071/api/answers/upload" `
  -Form @{
    file = Get-Item "C:\test-answer.jpg"
    examId = "21"
    questionId = "105"
  }

Write-Host "Extracted Text: $($uploadResponse.extractedText)"

# Step 2: Evaluate the answer
$evaluationBody = @{
    examId = 21
    questionId = 105
    studentAnswerText = $uploadResponse.extractedText
    extractedText = $uploadResponse.extractedText
    imageBlobPath = $uploadResponse.blobPath
} | ConvertTo-Json

$evalResponse = Invoke-RestMethod -Method Post `
  -Uri "http://localhost:7071/api/answers/evaluate" `
  -Body $evaluationBody `
  -ContentType "application/json"

Write-Host "Score: $($evalResponse.score)/$($evalResponse.maxMarks) ($($evalResponse.percentage)%)"
Write-Host "Feedback: $($evalResponse.feedback)"

# Step 3: Get all results for the exam
$examResults = Invoke-RestMethod -Method Get `
  -Uri "http://localhost:7071/api/evaluations/exam/21"

Write-Host "Total Score: $($examResults.totalScore)/$($examResults.totalMarks)"
Write-Host "Percentage: $($examResults.percentage)%"
```

---

## 📊 Sample Data for Testing

### Create Test Question (if needed)

```sql
-- Insert a test question for evaluation
INSERT INTO GeneratedQuestions (
    ChapterId, 
    Question, 
    Answer, 
    Marks, 
    QuestionType,
    Keywords,
    CreatedOn
)
VALUES (
    1, -- Use existing ChapterId
    'Define a matrix and explain its basic properties.',
    'A matrix is a rectangular array of numbers arranged in rows and columns. Basic properties include: 1) Order (m×n dimensions), 2) Element notation (aij), 3) Square matrix (m=n), 4) Determinant, 5) Transpose, 6) Identity matrix.',
    5,
    'Descriptive',
    'matrix,array,rows,columns,determinant,transpose,identity',
    GETDATE()
)

-- Get the QuestionId
SELECT TOP 1 * FROM GeneratedQuestions ORDER BY Id DESC

-- Create a test exam
INSERT INTO Exams (
    ChapterId,
    ExamName,
    TotalMarks,
    ExamType,
    CreatedOn
)
VALUES (
    1,
    'Linear Algebra Test - Chapter 1',
    50,
    'Chapter Test',
    GETDATE()
)

-- Link question to exam
INSERT INTO ExamQuestions (ExamId, QuestionId, CreatedOn)
VALUES (
    (SELECT TOP 1 Id FROM Exams ORDER BY Id DESC),
    (SELECT TOP 1 Id FROM GeneratedQuestions ORDER BY Id DESC),
    GETDATE()
)
```

---

## 🛠️ Troubleshooting

### Issue: "AZURE_FORM_RECOGNIZER_ENDPOINT must be set"

**Solution:** Add environment variable to `local.settings.json`:
```json
"AZURE_FORM_RECOGNIZER_ENDPOINT": "https://your-resource.cognitiveservices.azure.com/"
```

### Issue: "Question X not found"

**Solution:** Ensure the question exists in `GeneratedQuestions` table and is linked to the exam in `ExamQuestions`.

### Issue: OCR returns empty text

**Solutions:**
- Ensure image is clear and readable
- Check image format (supported: JPG, PNG, PDF)
- Verify image size is < 10MB
- Check Form Recognizer resource is active

### Issue: AI evaluation takes too long

**Solutions:**
- Check OpenAI API rate limits
- Verify `gpt-4o-mini` deployment exists
- Reduce MaxTokens in AiScoringService (currently 1000)

---

## 📈 Performance Optimization

### Current Configuration

```csharp
// OcrService: 3 retries with exponential backoff (500ms → 1000ms → 2000ms)
// AiScoringService: 3 retries, Temperature=0.3, MaxTokens=1000
// SqlDb: 3 retries with exponential backoff for all queries
```

### For Production

1. **Caching:** Cache frequently accessed questions/answers
2. **Batch Processing:** Evaluate multiple answers in parallel
3. **CDN:** Store answer images in CDN for faster access
4. **Monitoring:** Add Application Insights for tracking

---

## 💰 Cost Estimates

### Azure Document Intelligence
- **S0 Tier:** $1.50 per 1,000 pages
- **Typical usage:** ~100 answer sheets/day = $4.50/month

### Azure OpenAI (gpt-4o-mini)
- **Input tokens:** ~$0.15 per 1M tokens
- **Output tokens:** ~$0.60 per 1M tokens
- **Typical usage:** 100 evaluations/day ≈ $5-10/month

### Azure SQL
- **Basic tier:** ~$5/month (existing)
- **Storage:** Minimal additional cost

### Total: ~$15-20/month for evaluation system

---

## 🔐 Security Recommendations

1. **API Keys:** Store in Azure Key Vault (not in source code)
2. **Authentication:** Add Azure AD authentication to functions
3. **Rate Limiting:** Implement request throttling per user
4. **Input Validation:** Already implemented (file size, type checks)
5. **SQL Injection:** Using parameterized queries (Dapper)

---

## 🚀 Next Steps

### Phase 1: Core Testing (Now)
- [ ] Run SQL migration
- [ ] Configure environment variables
- [ ] Test UploadAnswer with sample image
- [ ] Test EvaluateAnswer with known answer
- [ ] Test GetEvaluationResults endpoints

### Phase 2: Integration
- [ ] Integrate with frontend UI
- [ ] Add user authentication
- [ ] Implement batch evaluation
- [ ] Add export to PDF functionality

### Phase 3: Enhancements
- [ ] Multi-language OCR support
- [ ] Handwriting recognition improvement
- [ ] Plagiarism detection
- [ ] Analytics dashboard

---

## 📞 Support

If you encounter issues:

1. **Check logs:** Azure Portal → Function App → Log Stream
2. **Verify config:** All environment variables set correctly
3. **Test services:** OCR and OpenAI endpoints accessible
4. **Database:** Run diagnostic queries to verify data

---

## ✅ Checklist

Before deploying to production:

- [ ] SQL table created with foreign keys
- [ ] Azure Form Recognizer resource provisioned
- [ ] Azure OpenAI gpt-4o-mini deployment verified
- [ ] All environment variables configured
- [ ] Build succeeds with 0 errors
- [ ] All 3 functions tested locally
- [ ] Sample data inserted for testing
- [ ] Error handling tested (invalid files, missing questions)
- [ ] Performance acceptable (< 5s per evaluation)
- [ ] Costs estimated and approved

---

## 🎓 Implementation Summary

✅ **SQL Schema:** EvaluatedAnswers table with FK relationships  
✅ **OCR Service:** Azure Document Intelligence integration  
✅ **AI Evaluation:** OpenAI GPT-4 scoring with keyword matching  
✅ **HTTP Functions:** 4 new endpoints (upload, evaluate, get×3)  
✅ **Error Handling:** Retry logic on all external services  
✅ **Documentation:** Comprehensive guides and examples  
✅ **Build Status:** **0 errors, 8 warnings (nullable refs only)**  

**The system is ready for testing!** 🚀
