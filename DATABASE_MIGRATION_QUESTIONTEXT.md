# Database Schema Update: Add QuestionText Column

## Problem
Questions from GeneratedExams table were not showing the actual question text in the evaluation results blob storage. The `WrittenQuestionEvaluation` table and model were missing the `QuestionText` field.

## Solution
Added `QuestionText` field to:
1. ✅ `WrittenQuestionEvaluation` model (C#)
2. ✅ `WrittenAnswerEvaluationService` - populates the field during evaluation
3. ✅ `WrittenSubmissionRepository` - saves the field to database
4. ✅ Deployed updated code to Azure
5. ⚠️  **Database schema needs to be updated**

## Required SQL Migration

Run the following SQL script in Azure SQL Database using Azure Portal Query Editor:

```sql
-- Add QuestionText column to WrittenQuestionEvaluations table
IF NOT EXISTS (
    SELECT 1 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_NAME = 'WrittenQuestionEvaluations' 
    AND COLUMN_NAME = 'QuestionText'
)
BEGIN
    ALTER TABLE WrittenQuestionEvaluations
    ADD QuestionText NVARCHAR(MAX) NULL;
    
    PRINT 'QuestionText column added successfully';
    
    -- Populate existing records from ExamQuestions table
    UPDATE wqe
    SET wqe.QuestionText = eq.QuestionText
    FROM WrittenQuestionEvaluations wqe
    INNER JOIN WrittenSubmissions ws ON wqe.WrittenSubmissionId = ws.Id
    INNER JOIN ExamQuestions eq ON eq.ExamId = ws.ExamId 
        AND eq.QuestionNumber = wqe.QuestionNumber
    WHERE wqe.QuestionText IS NULL;
    
    PRINT 'Updated existing records from ExamQuestions table';
    
    -- Set default for remaining null values
    UPDATE WrittenQuestionEvaluations
    SET QuestionText = 'Question text not available'
    WHERE QuestionText IS NULL;
    
    PRINT 'Set default text for remaining null values';
END
ELSE
BEGIN
    PRINT 'QuestionText column already exists.';
END
```

## How to Run the Migration

### Option 1: Azure Portal (Recommended)
1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to: `smartstudysqlsrv` → `smartstudy` database
3. Click on "Query editor"
4. Login with SQL authentication:
   - Username: `smartstudy-user`
   - Password: `SmartStudy2024@Pwd!` (or check app settings)
5. Paste the SQL script above
6. Click "Run"

### Option 2: PowerShell Script
If you have the correct database credentials in `local.settings.json`:
```powershell
.\run-sql-migration-questiontext.ps1
```

### Option 3: Azure Data Studio
1. Connect to: `smartstudysqlsrv.database.windows.net`
2. Database: `smartstudy`
3. Open file: `sql\05_AddQuestionTextColumn.sql`
4. Execute

## Verification

After running the migration, verify by running:

```sql
-- Check if column exists
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'WrittenQuestionEvaluations'
ORDER BY ORDINAL_POSITION;

-- Check sample data
SELECT TOP 5 
    QuestionNumber,
    LEFT(QuestionText, 50) AS QuestionTextPreview,
    AwardedScore,
    MaxScore
FROM WrittenQuestionEvaluations
ORDER BY EvaluatedAt DESC;
```

## Impact
After this migration:
- ✅ New evaluations will include the full question text
- ✅ Evaluation results in blob storage will have complete question data
- ✅ Frontend can display questions correctly without additional lookups
- ✅ Proper mapping between GeneratedExams questions and evaluation results

## Files Modified
- `Models/WrittenSubmissionModels.cs` - Added QuestionText property
- `Services/WrittenAnswerEvaluationService.cs` - Populate QuestionText in evaluation
- `Services/WrittenSubmissionRepository.cs` - Include QuestionText in INSERT statement
- `sql/05_AddQuestionTextColumn.sql` - Database migration script
