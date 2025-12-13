# AI Evaluation System - Deployment Checklist

## ✅ Pre-Deployment Verification

### Code Status
- [x] Build succeeds with 0 errors
- [x] Only 3 nullable warnings (in existing EmbeddingService, non-blocking)
- [x] All new files created successfully
- [x] Program.cs DI registrations complete
- [x] host.json updated with proper timeouts

### Files Created
1. [x] `SQL/02_CreateEvaluatedAnswersTable.sql` - Database schema
2. [x] `Models/EvaluationModels.cs` - DTOs (ScoringResult, EvaluateAnswerRequest, etc.)
3. [x] `Services/AiScoringService.cs` - AI scoring with Karnataka PUC prompt + fallback
4. [x] `Services/OcrService.cs` - Enhanced with cancellation support (UPDATED)
5. [x] `Functions/UploadAnswer.cs` - Upload + OCR endpoint
6. [x] `Functions/EvaluateAnswer.cs` - Evaluation endpoint
7. [x] `Functions/BatchEvaluate.cs` - Batch processing endpoint
8. [x] `Program.cs` - DI registrations (UPDATED)
9. [x] `host.json` - Timeout and concurrency config (UPDATED)
10. [x] `Examples/EvaluationExamples.ps1` - PowerShell examples
11. [x] `Examples/EvaluationExamples.sh` - cURL examples
12. [x] `Tests/AiScoringServiceTests.cs` - Unit tests
13. [x] `EVALUATION_SYSTEM_README.md` - Complete documentation

## 🔧 Configuration Steps

### 1. Database Setup

```sql
-- Run this SQL script on your Azure SQL database
-- File: SQL/02_CreateEvaluatedAnswersTable.sql

-- Connect to: school-chatbot-sql-10271900.database.windows.net
-- Database: StudentData

-- Creates:
-- - EvaluatedAnswers table
-- - Foreign keys to GeneratedExams and GeneratedQuestions
-- - 3 performance indexes
```

**Connection:**
```bash
sqlcmd -S school-chatbot-sql-10271900.database.windows.net -d StudentData -U your-username -P your-password -i SQL/02_CreateEvaluatedAnswersTable.sql
```

### 2. Environment Variables

Set these in Azure Function App Configuration:

```bash
# Azure OpenAI (Required for AI scoring)
AZURE_OPENAI_ENDPOINT=https://smartstudyai.openai.azure.com/
AZURE_OPENAI_KEY=your-openai-api-key-here
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o-mini

# Azure Form Recognizer (Required for OCR)
FORM_RECOGNIZER_ENDPOINT=https://your-form-recognizer.cognitiveservices.azure.com/
FORM_RECOGNIZER_KEY=your-form-recognizer-key-here

# SQL Database (Already exists, verify connection string)
SQL_CONNECTION_STRING=Server=school-chatbot-sql-10271900.database.windows.net;Database=StudentData;User Id=your-user;Password=your-password;Encrypt=True;

# Azure Blob Storage (Already exists, verify)
AzureWebJobsStorage=DefaultEndpointsProtocol=https;AccountName=your-storage-account;AccountKey=your-key;EndpointSuffix=core.windows.net
```

**Azure Portal Steps:**
1. Go to your Function App
2. Settings → Configuration
3. Click "New application setting" for each variable
4. Click "Save" and "Continue" to restart

### 3. Build and Deploy

```powershell
# Build locally first
cd C:\SmartStudyFunc\SmartStudyFunc
dotnet build -c Release

# Expected output: Build succeeded with 3 warning(s)

# Deploy to Azure
func azure functionapp publish your-function-app-name

# Or using VS Code: Right-click on folder → Deploy to Function App
```

### 4. Verify Blob Container

The system creates a `student-answers` container automatically, but you can pre-create it:

```powershell
# Using Azure CLI
az storage container create --name student-answers --account-name your-storage-account

# Or via Azure Portal:
# Storage Account → Containers → + Container → Name: "student-answers"
```

## 🧪 Testing Steps

### 1. Verify Function Endpoints

```bash
# Get your function app base URL
$BaseUrl = "https://your-function-app.azurewebsites.net/api"

# Get function key from Azure Portal:
# Function App → Functions → App keys → default (Function key)
$FunctionKey = "your-function-key"
```

### 2. Test Upload Endpoint

```powershell
# Test with a sample PDF
$uploadUrl = "$BaseUrl/answers/upload?code=$FunctionKey"
$form = @{
    examId = 1
    questionId = 1
    file = Get-Item -Path "C:\path\to\test.pdf"
}

$uploadResponse = Invoke-RestMethod -Uri $uploadUrl -Method Post -Form $form

# Expected: 200 OK with extractedText and blobPath
Write-Host "Extracted Text Length: $($uploadResponse.extractedText.Length)"
```

### 3. Test Evaluate Endpoint

```powershell
$evaluateUrl = "$BaseUrl/answers/evaluate?code=$FunctionKey"
$evaluateRequest = @{
    examId = 1
    questionId = 1
    studentAnswerText = "The derivative of x^2 is 2x using the power rule."
} | ConvertTo-Json

$evaluateResponse = Invoke-RestMethod -Uri $evaluateUrl -Method Post -Body $evaluateRequest -ContentType "application/json"

# Expected: 200 OK with score, feedback, etc.
Write-Host "Score: $($evaluateResponse.score)/$($evaluateResponse.maxMarks)"
```

### 4. Test Batch Endpoint

```powershell
$batchUrl = "$BaseUrl/answers/evaluate/batch?code=$FunctionKey"
$batchRequest = @{
    evaluations = @(
        @{
            examId = 1
            questionId = 1
            studentAnswerText = "Test answer 1"
        },
        @{
            examId = 1
            questionId = 2
            studentAnswerText = "Test answer 2"
        }
    )
} | ConvertTo-Json -Depth 5

$batchResponse = Invoke-RestMethod -Uri $batchUrl -Method Post -Body $batchRequest -ContentType "application/json"

# Expected: 200 OK with results array
Write-Host "Processed: $($batchResponse.totalProcessed)/$($batchResponse.totalRequested)"
```

### 5. Verify Database

```sql
-- Check evaluations were saved
SELECT TOP 10 
    Id, ExamId, QuestionId, Score, MaxMarks, 
    Feedback, EvaluatedOn
FROM EvaluatedAnswers
ORDER BY EvaluatedOn DESC;

-- Verify foreign keys work
SELECT 
    ea.Id, ea.Score,
    ge.ExamName,
    gq.QuestionText
FROM EvaluatedAnswers ea
JOIN GeneratedExams ge ON ea.ExamId = ge.Id
JOIN GeneratedQuestions gq ON ea.QuestionId = gq.Id;
```

## 📊 Monitoring

### 1. Check Application Insights

**Azure Portal:**
1. Function App → Application Insights
2. Logs → Run query:

```kusto
traces
| where message contains "scoring" or message contains "evaluation"
| where timestamp > ago(1h)
| order by timestamp desc
| project timestamp, severityLevel, message, customDimensions
```

### 2. Monitor Function Invocations

```kusto
requests
| where timestamp > ago(1h)
| where name startswith "UploadAnswer" or name startswith "EvaluateAnswer"
| summarize count(), avg(duration) by name, resultCode
| order by name
```

### 3. Check for Errors

```kusto
exceptions
| where timestamp > ago(1h)
| where cloud_RoleName contains "SmartStudyFunc"
| project timestamp, operation_Name, problemId, outerMessage
| order by timestamp desc
```

## 🐛 Troubleshooting

### Issue: "Question not found" error
**Solution:** Ensure question exists in GeneratedQuestions table with IdealAnswer, Marks, and Keywords populated.

```sql
-- Verify question data
SELECT Id, QuestionText, IdealAnswer, Marks, Keywords
FROM GeneratedQuestions
WHERE Id = 1;
```

### Issue: OCR extraction fails
**Solution:** 
1. Verify FORM_RECOGNIZER_ENDPOINT and FORM_RECOGNIZER_KEY are set
2. Check file type is PDF, JPG, JPEG, or PNG
3. Verify file size < 10MB
4. Check Application Insights logs for specific error

### Issue: AI scoring always uses fallback
**Solution:**
1. Verify AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_KEY, AZURE_OPENAI_DEPLOYMENT_NAME are set
2. Check OpenAI deployment name is correct (gpt-4o-mini)
3. Verify OpenAI quota is not exceeded
4. Check Application Insights for 429 or 401 errors

### Issue: Batch evaluation timeout
**Solution:**
1. Reduce batch size (system throttles to 3 concurrent)
2. Increase functionTimeout in host.json (currently 10 minutes)
3. Check if database connections are being exhausted

### Issue: Database connection failures
**Solution:**
1. Verify SQL_CONNECTION_STRING is correct
2. Check firewall rules allow Azure Function App IP
3. Verify database is not at capacity (DTU limits)

## 📈 Performance Optimization

### 1. Scale Out Function App
- App Service Plan: Scale up to Premium or higher
- Scale out: Increase instance count during peak hours

### 2. Database Optimization
- Monitor DTU usage
- Add indexes if query performance degrades:
  ```sql
  CREATE INDEX IX_EvaluatedAnswers_StudentAnswer ON EvaluatedAnswers(StudentAnswer);
  ```

### 3. Blob Storage
- Use CDN for frequently accessed answer images
- Set lifecycle policies to archive old evaluations

## 🔐 Security Checklist

- [x] Function keys enforced (AuthorizationLevel.Function)
- [x] SQL injection prevented (Dapper parameterized queries)
- [ ] Enable Managed Identity for Azure SQL connection (optional enhancement)
- [ ] Enable CORS only for trusted domains in production
- [ ] Rotate function keys regularly
- [ ] Enable Azure Front Door WAF (optional)

## 🎯 Success Criteria

After deployment, verify:

1. ✅ UploadAnswer endpoint returns OCR-extracted text
2. ✅ EvaluateAnswer endpoint returns score with AI feedback
3. ✅ BatchEvaluate processes multiple answers concurrently
4. ✅ Fallback scoring activates when OpenAI unavailable
5. ✅ Evaluations saved to EvaluatedAnswers table
6. ✅ Application Insights logs show detailed trace data
7. ✅ No compilation errors in deployed code
8. ✅ Response times < 5 seconds for single evaluation

## 📞 Support Contacts

- **Azure OpenAI Issues:** Check quota and deployment status
- **Form Recognizer Issues:** Verify endpoint and key
- **Database Issues:** Check connection string and firewall rules
- **Function App Issues:** Review Application Insights logs

## 🚀 Post-Deployment

### Monitor for 24 Hours
- Check Application Insights for errors
- Monitor database query performance
- Verify blob storage usage
- Check OpenAI API quota consumption

### Collect Metrics
- Average evaluation time
- AI vs fallback scoring ratio
- Common error patterns
- User feedback on scoring accuracy

### Iterate
- Adjust Karnataka PUC prompt based on teacher feedback
- Tune keyword matching algorithm
- Optimize retry delays based on actual error rates
- Add custom indexes based on query patterns

---

**Deployment Owner:** ____________________

**Deployment Date:** ____________________

**Verified By:** ____________________

**Status:** ⬜ Ready ⬜ In Progress ⬜ Complete ⬜ Verified
