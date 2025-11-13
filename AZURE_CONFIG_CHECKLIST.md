# Azure Production Configuration Checklist

## Required Application Settings in Azure Function App

Go to: Azure Portal → Function App `studyai-ingestion-345` → Settings → Configuration → Application settings

### 1. Database Connection
```
ConnectionStrings__SqlDb = Server=tcp:school-chatbot-sql-10271900.database.windows.net,1433;Initial Catalog=school-ai-chatbot;User ID=schooladmin;Password=<YOUR_DB_PASSWORD>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

### 2. Azure OpenAI Settings
```
USE_REAL_EMBEDDINGS = true
AzureOpenAI__Endpoint = https://smartstudyai.openai.azure.com/
AzureOpenAI__ApiKey = <YOUR_AZURE_OPENAI_API_KEY>
AzureOpenAI__EmbeddingDeployment = text-embedding-3-small
AzureOpenAI__DeploymentChat = gpt-4o-mini
```

### 3. Storage Account (should already exist)
```
AzureWebJobsStorage = <connection string from Azure>
```

### 4. Database Schema Migration

**CRITICAL**: Run the SQL migration on your Azure SQL database:

```sql
-- Connect to Azure SQL database: school-chatbot-sql-10271900.database.windows.net
-- Database: school-ai-chatbot

-- Run this script:
USE [school-ai-chatbot];

ALTER TABLE UploadedFiles
ADD 
    ClassName NVARCHAR(100) NULL,
    Subject NVARCHAR(100) NULL,
    Chapter NVARCHAR(100) NULL;

CREATE NONCLUSTERED INDEX IX_UploadedFiles_Metadata
ON UploadedFiles (ClassName, Subject, Chapter)
INCLUDE (FileName, UploadedOn);
```

### 5. Check Application Insights Logs

After deployment, check errors:
1. Go to Function App → Monitoring → Application Insights
2. Click "Logs" 
3. Run query:
```kusto
traces
| where severityLevel >= 3  // Error and above
| where timestamp > ago(1h)
| order by timestamp desc
```

### 6. Common Issues

**If only UploadedFiles has records but not Chunks/Embeddings:**

1. **Check Azure OpenAI deployment exists**
   - Portal → Azure OpenAI `smartstudyai`
   - Deployments → Verify `text-embedding-3-small` exists

2. **Check database tables exist**
   ```sql
   SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES 
   WHERE TABLE_SCHEMA = 'dbo'
   ORDER BY TABLE_NAME;
   ```
   Should show: Chunks, Embeddings, UploadedFiles

3. **Check for errors in logs**
   - Application Insights → Failures
   - Look for SQL timeout, embedding errors, etc.

4. **Verify connection strings**
   - Test SQL connection from Azure Portal query editor
   - Verify Azure OpenAI key is valid

### 7. Test After Deployment

Upload a test file and verify:
```sql
-- Check file was uploaded
SELECT TOP 10 * FROM UploadedFiles ORDER BY Id DESC;

-- Check chunks were created
SELECT TOP 10 * FROM Chunks ORDER BY Id DESC;

-- Check embeddings were created
SELECT TOP 10 ChunkId, LEN(EmbeddingVector) as VectorSize 
FROM Embeddings ORDER BY ChunkId DESC;
```
