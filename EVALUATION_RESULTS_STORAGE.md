# Evaluation Results Permanent Storage

## Overview
Evaluation results are now permanently stored in Azure Blob Storage as JSON files, ensuring students can access their detailed results anytime without risk of data loss.

## Implementation Details

### Storage Location
- **Container**: `evaluation-results`
- **Path Format**: `{examId}/{submissionId}/evaluation-result.json`
- **Content Type**: `application/json`
- **Format**: Pretty-printed JSON for readability

### Data Saved
Each evaluation result JSON contains:
```json
{
  "writtenSubmissionId": "guid",
  "examId": "string",
  "studentId": "string",
  "totalScore": 45.5,
  "maxPossibleScore": 100,
  "percentage": 45.5,
  "grade": "F",
  "questionEvaluations": [
    {
      "id": "guid",
      "writtenSubmissionId": "guid",
      "questionId": "guid",
      "questionNumber": 1,
      "extractedAnswer": "Student's answer text...",
      "modelAnswer": "Expected answer...",
      "maxScore": 20,
      "awardedScore": 9.5,
      "feedback": "Detailed feedback...",
      "rubricBreakdown": "Step-wise marking...",
      "evaluatedAt": "2025-12-15T11:00:00Z"
    }
  ],
  "evaluatedAt": "2025-12-15T11:00:00Z"
}
```

### Database Schema
New column added to `WrittenSubmissions` table:
- **Column**: `EvaluationResultBlobPath` (NVARCHAR(500), NULL)
- **Index**: `IX_WrittenSubmissions_EvaluationResultBlobPath` (filtered, non-clustered)
- **Purpose**: Store blob path for quick retrieval

### Processing Flow
1. **OCR Extraction**: Student answer sheet → Text extraction
2. **AI Evaluation**: Text → Question-by-question evaluation with rubrics
3. **Blob Storage**: Evaluation result → JSON saved to blob (NEW!)
4. **Database Update**: Metadata + blob path saved to SQL
5. **Status**: Submission marked as Completed (Status=3)

### Data Retention
- ✅ **Evaluation Results**: NEVER deleted (permanent storage)
- ⚠️ **Answer Sheet Images**: Deleted after retention period (default: 30 days)
- ⚠️ **OCR Extracted Text**: Deleted after retention period (default: 30 days)
- ✅ **Database Records**: Retained permanently (only blob paths deleted)

### Cleanup Function Behavior
`CleanupOldWrittenSubmissions` function:
- Runs daily at 2:00 AM UTC
- Deletes answer sheet blobs older than retention period
- Deletes OCR text blobs older than retention period
- **DOES NOT delete evaluation result blobs** ✅
- Updates `BlobsDeleted` flag in database

### Student Access
Students can retrieve their evaluation results through:
1. **GetSubmissionStatus API**: Returns blob path
2. **Direct Blob URL**: Download JSON with SAS token
3. **GetEvaluationResults API**: Retrieves and parses JSON

### Benefits
- 📊 **Permanent Record**: Students can review results anytime
- 💾 **Data Integrity**: No accidental deletion of evaluation data
- 🔍 **Auditability**: Complete evaluation history preserved
- 📱 **Accessibility**: JSON format easy to parse/display in apps
- 💰 **Cost Effective**: JSON files are tiny (~5-20KB each)

### Migration Applied
```sql
-- Added column
ALTER TABLE WrittenSubmissions
ADD EvaluationResultBlobPath NVARCHAR(500) NULL;

-- Added index for performance
CREATE NONCLUSTERED INDEX IX_WrittenSubmissions_EvaluationResultBlobPath
ON WrittenSubmissions(EvaluationResultBlobPath)
WHERE EvaluationResultBlobPath IS NOT NULL;
```

### Files Modified
1. `ProcessWrittenSubmission.cs`:
   - Added `SaveEvaluationResultToBlobAsync()` method
   - Modified Phase 5 to save results to blob before database
   - Added error handling for blob save failures

2. `WrittenSubmissionRepository.cs`:
   - Updated `SaveEvaluationResultAsync()` signature
   - Added `resultBlobPath` parameter
   - Updated SQL to save blob path

3. `sql/06_AddEvaluationResultBlobPath.sql`:
   - Migration script for database schema change

### Testing
After deployment:
1. Submit a new answer sheet for evaluation
2. Check database: `EvaluationResultBlobPath` should be populated
3. Check blob storage: `evaluation-results` container should contain JSON
4. Verify JSON contains complete evaluation details
5. Confirm cleanup function does NOT delete evaluation results

### Example Blob Path
```
evaluation-results/SAMPLE-EXAM-001/a1b2c3d4-e5f6-7890-abcd-ef1234567890/evaluation-result.json
```

### Deployment Status
- ✅ Code deployed to production
- ✅ Database migration applied
- ✅ Function app restarted
- ✅ OCR configuration verified (Google Vision API)
- ✅ Ready for testing

## Next Steps
1. Test with real submission to verify blob creation
2. Create API endpoint to retrieve evaluation JSON by URL
3. Update mobile/web app to display evaluation results from blob
4. Monitor blob storage costs and usage
