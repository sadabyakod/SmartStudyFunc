# Evaluation Results Storage - Backend API Reference

## Overview
This document provides backend implementation reference for the **Permanent Evaluation Results Storage** feature. All evaluation results are stored as JSON in Azure Blob Storage for permanent student access.

---

## 📁 Storage Architecture

### Blob Storage Structure
```
evaluation-results/                          (Container)
├── SAMPLE-EXAM-001/                        (Exam ID)
│   ├── a1b2c3d4-e5f6-7890-abcd-ef1234567890/  (Submission ID)
│   │   └── evaluation-result.json         (Result file)
│   └── b2c3d4e5-f6a7-8901-bcde-f1234567890a/
│       └── evaluation-result.json
└── Karnataka_2nd_PUC_Math_2024_25/
    └── c3d4e5f6-a7b8-9012-cdef-123456789abc/
        └── evaluation-result.json
```

### Database Schema Reference
```sql
-- WrittenSubmissions table columns
CREATE TABLE WrittenSubmissions (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    ExamId NVARCHAR(200) NOT NULL,
    StudentId NVARCHAR(200) NOT NULL,
    Status INT NOT NULL,                    -- 0=Uploaded, 1=OCR, 2=Evaluating, 3=Completed, 4=Failed
    TotalScore DECIMAL(10,2),
    MaxPossibleScore DECIMAL(10,2),
    Percentage DECIMAL(5,2),
    Grade NVARCHAR(10),
    EvaluationResultBlobPath NVARCHAR(500), -- ✨ NEW: Blob path for evaluation result
    SubmittedAt DATETIME2 NOT NULL,
    EvaluatedAt DATETIME2,
    -- ... other columns
);

-- Index for fast retrieval
CREATE NONCLUSTERED INDEX IX_WrittenSubmissions_EvaluationResultBlobPath
ON WrittenSubmissions(EvaluationResultBlobPath)
WHERE EvaluationResultBlobPath IS NOT NULL;
```

---

## 📄 JSON Response Format

### Complete Evaluation Result JSON
```json
{
  "writtenSubmissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "examId": "SAMPLE-EXAM-001",
  "studentId": "STU-12345",
  "totalScore": 45.5,
  "maxPossibleScore": 100.0,
  "percentage": 45.5,
  "grade": "F",
  "evaluatedAt": "2025-12-15T11:30:00.123Z",
  "questionEvaluations": [
    {
      "id": "eval-uuid-1",
      "writtenSubmissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "questionId": "q-uuid-1",
      "questionNumber": 1,
      "extractedAnswer": "Photosynthesis is the process by which plants make food using sunlight...",
      "modelAnswer": "Photosynthesis is the process where plants convert light energy into chemical energy...",
      "maxScore": 20.0,
      "awardedScore": 9.5,
      "feedback": "Good understanding of basic concept. Missing details about chlorophyll and chemical equation.",
      "rubricBreakdown": "Step 1: Definition (2/3) ✓\nStep 2: Process explanation (2/4) ✓\nStep 3: Chlorophyll role (0/3) ✗\nStep 4: Chemical equation (0/5) ✗\nStep 5: Products (3/3) ✓\nStep 6: Importance (2.5/2) ✓",
      "evaluatedAt": "2025-12-15T11:30:00.123Z"
    },
    {
      "id": "eval-uuid-2",
      "writtenSubmissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "questionId": "q-uuid-2",
      "questionNumber": 2,
      "extractedAnswer": "Newton's first law states that objects at rest stay at rest...",
      "modelAnswer": "Newton's First Law (Law of Inertia): An object at rest stays at rest...",
      "maxScore": 15.0,
      "awardedScore": 5.8,
      "feedback": "Partial explanation provided. Missing mathematical formulation and real-world examples.",
      "rubricBreakdown": "Concept: 3/5 marks\nExplanation: 2/5 marks\nExample: 0.8/5 marks",
      "evaluatedAt": "2025-12-15T11:30:00.123Z"
    }
  ]
}
```

---

## 🔧 Backend Implementation

### 1. Save Evaluation Result to Blob (ProcessWrittenSubmission.cs)

```csharp
/// <summary>
/// Saves evaluation results as JSON to blob storage for permanent student access.
/// </summary>
private async Task<string> SaveEvaluationResultToBlobAsync(
    Guid submissionId,
    string examId,
    WrittenEvaluationResult result,
    CancellationToken cancellationToken)
{
    var containerName = "evaluation-results";
    var blobPath = $"{examId}/{submissionId}/evaluation-result.json";

    var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
    await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

    var blobClient = containerClient.GetBlobClient(blobPath);

    // Serialize with pretty formatting for readability
    var jsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    var json = JsonSerializer.Serialize(result, jsonOptions);

    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
    
    var uploadOptions = new BlobUploadOptions
    {
        HttpHeaders = new BlobHttpHeaders
        {
            ContentType = "application/json"
        }
    };
    
    await blobClient.UploadAsync(stream, uploadOptions, cancellationToken);

    return $"{containerName}/{blobPath}";
}
```

### 2. Save Blob Path to Database (WrittenSubmissionRepository.cs)

```csharp
public async Task SaveEvaluationResultAsync(
    WrittenEvaluationResult result,
    string? resultBlobPath = null,
    long? processingTimeMs = null,
    CancellationToken cancellationToken = default)
{
    const string sql = @"
        UPDATE WrittenSubmissions
        SET Status = @Status,
            EvaluatedAt = @EvaluatedAt,
            TotalScore = @TotalScore,
            MaxPossibleScore = @MaxPossibleScore,
            Percentage = @Percentage,
            Grade = @Grade,
            EvaluationResultBlobPath = @ResultBlobPath,
            EvaluationProcessingTimeMs = @ProcessingTimeMs
        WHERE Id = @Id";

    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    
    await using var cmd = new SqlCommand(sql, connection);
    cmd.Parameters.AddWithValue("@Id", result.WrittenSubmissionId);
    cmd.Parameters.AddWithValue("@Status", (int)WrittenSubmissionStatus.Completed);
    cmd.Parameters.AddWithValue("@EvaluatedAt", result.EvaluatedAt);
    cmd.Parameters.AddWithValue("@TotalScore", result.TotalScore);
    cmd.Parameters.AddWithValue("@MaxPossibleScore", result.MaxPossibleScore);
    cmd.Parameters.AddWithValue("@Percentage", result.Percentage);
    cmd.Parameters.AddWithValue("@Grade", CalculateGrade(result.Percentage));
    cmd.Parameters.AddWithValue("@ResultBlobPath", (object?)resultBlobPath ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@ProcessingTimeMs", (object?)processingTimeMs ?? DBNull.Value);
    
    await cmd.ExecuteNonQueryAsync(cancellationToken);
}
```

---

## 🌐 API Endpoints for Integration

### 1. Get Submission Status (Existing - Now Returns Blob Path)

**Endpoint:** `GET /api/submissions/{submissionId}/status`

**Response:**
```json
{
  "submissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": 3,
  "statusText": "Completed",
  "totalScore": 45.5,
  "maxScore": 100.0,
  "percentage": 45.5,
  "grade": "F",
  "evaluationResultBlobPath": "evaluation-results/SAMPLE-EXAM-001/a1b2c3d4-e5f6-7890-abcd-ef1234567890/evaluation-result.json",
  "submittedAt": "2025-12-15T10:00:00Z",
  "evaluatedAt": "2025-12-15T11:30:00Z"
}
```

### 2. Get Evaluation Result (NEW - Fetch from Blob)

**Endpoint:** `GET /api/evaluations/{submissionId}/result`

**Implementation Example:**
```csharp
[Function("GetEvaluationResult")]
public async Task<IActionResult> GetEvaluationResult(
    [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evaluations/{submissionId}/result")] 
    HttpRequest req,
    string submissionId,
    CancellationToken ct)
{
    // 1. Get blob path from database
    var submission = await _repository.GetSubmissionByIdAsync(Guid.Parse(submissionId), ct);
    
    if (submission == null)
        return new NotFoundObjectResult(new { Error = "Submission not found" });
    
    if (string.IsNullOrEmpty(submission.EvaluationResultBlobPath))
        return new NotFoundObjectResult(new { Error = "Evaluation result not available" });
    
    // 2. Download JSON from blob
    var parts = submission.EvaluationResultBlobPath.Split('/', 2);
    var containerName = parts[0];
    var blobPath = parts[1];
    
    var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
    var blobClient = containerClient.GetBlobClient(blobPath);
    
    if (!await blobClient.ExistsAsync(ct))
        return new NotFoundObjectResult(new { Error = "Evaluation result file not found" });
    
    // 3. Read and return JSON
    var response = await blobClient.DownloadContentAsync(ct);
    var json = response.Value.Content.ToString();
    
    return new OkObjectResult(JsonSerializer.Deserialize<WrittenEvaluationResult>(json));
}
```

### 3. Get Evaluation Result with SAS Token (NEW - Direct Blob Access)

**Endpoint:** `GET /api/evaluations/{submissionId}/download-url`

**Response:**
```json
{
  "submissionId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "downloadUrl": "https://stsmartstudydev.blob.core.windows.net/evaluation-results/SAMPLE-EXAM-001/abc.../evaluation-result.json?sv=2021-06-08&se=2025-12-15T23%3A59%3A59Z&sr=b&sp=r&sig=...",
  "expiresAt": "2025-12-15T23:59:59Z"
}
```

**Implementation:**
```csharp
[Function("GetEvaluationDownloadUrl")]
public async Task<IActionResult> GetEvaluationDownloadUrl(
    [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evaluations/{submissionId}/download-url")] 
    HttpRequest req,
    string submissionId,
    CancellationToken ct)
{
    var submission = await _repository.GetSubmissionByIdAsync(Guid.Parse(submissionId), ct);
    
    if (submission == null || string.IsNullOrEmpty(submission.EvaluationResultBlobPath))
        return new NotFoundObjectResult(new { Error = "Evaluation result not available" });
    
    var parts = submission.EvaluationResultBlobPath.Split('/', 2);
    var containerClient = _blobServiceClient.GetBlobContainerClient(parts[0]);
    var blobClient = containerClient.GetBlobClient(parts[1]);
    
    // Generate SAS token valid for 24 hours
    var sasBuilder = new BlobSasBuilder
    {
        BlobContainerName = parts[0],
        BlobName = parts[1],
        Resource = "b",
        StartsOn = DateTimeOffset.UtcNow,
        ExpiresOn = DateTimeOffset.UtcNow.AddHours(24)
    };
    sasBuilder.SetPermissions(BlobSasPermissions.Read);
    
    var sasToken = blobClient.GenerateSasUri(sasBuilder);
    
    return new OkObjectResult(new
    {
        SubmissionId = submissionId,
        DownloadUrl = sasToken.ToString(),
        ExpiresAt = sasBuilder.ExpiresOn
    });
}
```

---

## 📱 Mobile/Frontend Integration

### Example: Fetch and Display Results

```javascript
// 1. Get submission status
const statusResponse = await fetch(
  `${API_BASE}/api/submissions/${submissionId}/status?code=${FUNCTION_KEY}`
);
const status = await statusResponse.json();

if (status.status === 3 && status.evaluationResultBlobPath) {
  // 2. Option A: Get result through API (proxied)
  const resultResponse = await fetch(
    `${API_BASE}/api/evaluations/${submissionId}/result?code=${FUNCTION_KEY}`
  );
  const evaluation = await resultResponse.json();
  
  // Display results
  console.log(`Total Score: ${evaluation.totalScore}/${evaluation.maxPossibleScore}`);
  console.log(`Grade: ${evaluation.grade}`);
  
  evaluation.questionEvaluations.forEach(q => {
    console.log(`Q${q.questionNumber}: ${q.awardedScore}/${q.maxScore}`);
    console.log(`Feedback: ${q.feedback}`);
  });
  
  // OR Option B: Get direct download URL
  const urlResponse = await fetch(
    `${API_BASE}/api/evaluations/${submissionId}/download-url?code=${FUNCTION_KEY}`
  );
  const { downloadUrl } = await urlResponse.json();
  
  // Download JSON directly from blob
  const blobResponse = await fetch(downloadUrl);
  const evaluation = await blobResponse.json();
}
```

### Example: React Component

```jsx
function EvaluationResults({ submissionId }) {
  const [evaluation, setEvaluation] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    async function loadResults() {
      try {
        const response = await fetch(
          `${API_BASE}/api/evaluations/${submissionId}/result?code=${FUNCTION_KEY}`
        );
        const data = await response.json();
        setEvaluation(data);
      } catch (error) {
        console.error('Failed to load evaluation:', error);
      } finally {
        setLoading(false);
      }
    }
    loadResults();
  }, [submissionId]);

  if (loading) return <Spinner />;
  if (!evaluation) return <ErrorMessage />;

  return (
    <div>
      <h2>Evaluation Results</h2>
      <div className="summary">
        <p>Total Score: {evaluation.totalScore} / {evaluation.maxPossibleScore}</p>
        <p>Percentage: {evaluation.percentage}%</p>
        <p>Grade: {evaluation.grade}</p>
      </div>
      
      {evaluation.questionEvaluations.map(q => (
        <QuestionResult key={q.id} question={q} />
      ))}
    </div>
  );
}
```

---

## 🧪 Testing

### 1. Test Blob Creation

```powershell
# Submit test evaluation
$body = @{
    examId = "SAMPLE-EXAM-001"
    questionId = "q1-uuid"
    studentAnswerText = "Test answer text..."
} | ConvertTo-Json

$result = Invoke-RestMethod `
    -Uri "https://smartstudy-func.azurewebsites.net/api/answers/evaluate?code=$KEY" `
    -Method POST `
    -Body $body `
    -ContentType "application/json"

Write-Host "Submission ID: $($result.submissionId)"
```

### 2. Verify Blob Storage

```powershell
# Check if evaluation result exists in blob
az storage blob exists `
    --account-name stsmartstudydev `
    --container-name evaluation-results `
    --name "SAMPLE-EXAM-001/$submissionId/evaluation-result.json" `
    --auth-mode login
```

### 3. Download and Verify JSON

```powershell
# Download evaluation result
az storage blob download `
    --account-name stsmartstudydev `
    --container-name evaluation-results `
    --name "SAMPLE-EXAM-001/$submissionId/evaluation-result.json" `
    --file "./result.json" `
    --auth-mode login

# View content
Get-Content "./result.json" | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

### 4. SQL Verification

```sql
-- Check blob path is saved
SELECT 
    Id,
    ExamId,
    StudentId,
    TotalScore,
    Grade,
    EvaluationResultBlobPath,
    EvaluatedAt
FROM WrittenSubmissions
WHERE Id = 'submission-uuid'
  AND EvaluationResultBlobPath IS NOT NULL;
```

---

## 🔒 Security Considerations

### 1. Access Control
- **Function Level**: Use `AuthorizationLevel.Function` for API keys
- **SAS Tokens**: Generate time-limited tokens (24 hours recommended)
- **Student Validation**: Verify student owns the submission before returning results

### 2. Data Privacy
- Evaluation results contain sensitive student data
- Implement student authentication before exposing results
- Log all result access attempts

### 3. Example: Secure Endpoint

```csharp
[Function("GetEvaluationResult")]
public async Task<IActionResult> GetEvaluationResult(
    [HttpTrigger(AuthorizationLevel.Function, "get", Route = "evaluations/{submissionId}/result")] 
    HttpRequest req,
    string submissionId,
    CancellationToken ct)
{
    // 1. Validate student authentication
    var studentId = req.Headers["X-Student-Id"].ToString();
    if (string.IsNullOrEmpty(studentId))
        return new UnauthorizedResult();
    
    // 2. Get submission
    var submission = await _repository.GetSubmissionByIdAsync(Guid.Parse(submissionId), ct);
    
    // 3. Verify ownership
    if (submission.StudentId != studentId)
        return new ForbidResult();
    
    // 4. Return result
    // ... rest of implementation
}
```

---

## 📊 Monitoring and Logging

### Key Metrics to Track
```csharp
// Log blob save success
_logger.LogInformation(
    "[RESULT_SAVED_TO_BLOB] SubmissionId={SubmissionId}, BlobPath={BlobPath}, SizeBytes={Size}",
    submissionId, resultBlobPath, jsonBytes.Length);

// Log blob save failure
_logger.LogWarning(ex,
    "[RESULT_BLOB_SAVE_FAILED] SubmissionId={SubmissionId}. Database save will continue.",
    submissionId);

// Log result retrieval
_logger.LogInformation(
    "[RESULT_RETRIEVED] SubmissionId={SubmissionId}, StudentId={StudentId}, ResponseTimeMs={Time}",
    submissionId, studentId, stopwatch.ElapsedMilliseconds);
```

### Application Insights Query
```kusto
traces
| where timestamp > ago(24h)
| where message contains "RESULT_SAVED_TO_BLOB" or message contains "RESULT_RETRIEVED"
| project timestamp, message, customDimensions.SubmissionId
| order by timestamp desc
```

---

## 🚀 Deployment Checklist

- [x] Database migration applied (`EvaluationResultBlobPath` column added)
- [x] Code deployed to Azure Functions
- [x] Blob container `evaluation-results` created
- [x] Function app restarted
- [ ] API endpoints tested (GetEvaluationResult, GetDownloadUrl)
- [ ] Frontend integration completed
- [ ] Mobile app updated to use new endpoints
- [ ] Security review completed
- [ ] Monitoring dashboards configured
- [ ] Documentation shared with team

---

## 📞 Support

For questions or issues:
- **Backend Issues**: Check Azure Function logs in Application Insights
- **Blob Storage Issues**: Verify container permissions and storage account keys
- **Database Issues**: Check `EvaluationResultBlobPath` column exists
- **API Issues**: Test endpoints with Postman/curl and verify function keys

---

## 📚 Related Documentation
- [Evaluation System README](./README_ANSWER_EVALUATION.md)
- [Evaluation Pipeline](./README_EVALUATION_PIPELINE.md)
- [Storage Architecture](./EVALUATION_RESULTS_STORAGE.md)
- [Azure Functions Documentation](https://learn.microsoft.com/en-us/azure/azure-functions/)
- [Azure Blob Storage SDK](https://learn.microsoft.com/en-us/azure/storage/blobs/)
