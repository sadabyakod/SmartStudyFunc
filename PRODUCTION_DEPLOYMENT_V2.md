# PRODUCTION DEPLOYMENT GUIDE - V2 Evaluation System
## SmartStudyFunc - Enhanced Evaluation Engines

**Date**: December 20, 2024  
**Version**: V2.1 - Production Hardened  
**Status**: READY FOR DEPLOYMENT

---

## 🎯 Overview

This guide covers deployment of the **production-hardened V2 evaluation system** with:
- ✅ Enhanced question classification (weighted keywords, confidence scoring)
- ✅ Improved mathematics engine (variable aliasing, better symbolic equivalence)
- ✅ Comprehensive unit validation (SI + non-SI conversions)
- ✅ Syllabus caching service (reduces Blob Storage calls)
- ✅ Audit logging system (full traceability)

---

## 📋 Pre-Deployment Checklist

### 1. Database Setup
```bash
# Run the audit log table creation script
sqlcmd -S <server>.database.windows.net -d <database> -U <username> -P <password> -i sql/CreateEvaluationAuditLogTable.sql
```

**Expected Output**: `Created table: EvaluationAuditLog` + 6 indexes

### 2. Azure Blob Storage
Ensure `syllabus` container exists with structure:
```
syllabus/
  ├── class-6/
  │   ├── mathematics.txt
  │   ├── physics.txt
  │   ├── chemistry.txt
  │   ├── biology.txt
  │   └── socialscience.txt
  ├── class-7/
  │   └── ... (same subjects)
  ...
  └── class-12/
      └── ... (same subjects)
```

**Action**: Upload syllabus content for all subjects and class levels.

### 3. Configuration Validation
Check `local.settings.json` (or App Settings in Azure Portal):

```json
{
  "Values": {
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "SQL_CONNECTION_STRING": "<your-sql-connection>",
    "OPENAI_API_KEY": "<your-openai-key>",
    "OPENAI_ENDPOINT": "https://<your-openai>.openai.azure.com",
    "OPENAI_DEPLOYMENT_NAME": "gpt-4o-mini",
    "OPENAI_EMBEDDING_DEPLOYMENT": "text-embedding-3-large",
    "AZURE_STORAGE_CONNECTION": "<your-storage-connection>",
    "SYLLABUS_CONTAINER": "syllabus"
  }
}
```

---

## 🚀 Deployment Steps

### Step 1: Update DI Registration

Add new services to `Program.cs`:

```csharp
// Enhanced Evaluation Services
services.AddSingleton<EnhancedQuestionClassifier>();
services.AddSingleton<SyllabusCacheService>();

// Audit Logger (requires SQL connection)
services.AddSingleton<EvaluationAuditLogger>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<EvaluationAuditLogger>>();
    var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");
    return new EvaluationAuditLogger(logger, connectionString);
});

// Memory Cache for syllabus
services.AddMemoryCache(options =>
{
    options.SizeLimit = 100 * 1024 * 1024; // 100 MB cache limit
});
```

### Step 2: Build Project

```bash
cd c:\SmartStudyFunc
dotnet build -c Release
```

**Expected**: `Build succeeded. 0 Error(s)`

### Step 3: Run Tests (Optional but Recommended)

```bash
# Test enhanced classifier
dotnet test --filter "FullyQualifiedName~EnhancedQuestionClassifierTests"

# Test unit validation
dotnet test --filter "FullyQualifiedName~UnitValidationHelpersTests"

# Test math helpers
dotnet test --filter "FullyQualifiedName~MathEvaluationHelpersTests"
```

### Step 4: Deploy to Azure

```bash
func azure functionapp publish smartstudy-func
```

**Monitor Output**: Look for "Deployment completed successfully"

### Step 5: Pre-Warm Syllabus Cache

After deployment, call a warm-up endpoint or create a timer trigger:

```csharp
[Function("WarmUpSyllabusCache")]
public async Task<HttpResponseData> WarmUpCache(
    [HttpTrigger(AuthorizationLevel.Admin, "post")] HttpRequestData req,
    [FromServices] SyllabusCacheService cacheService)
{
    await cacheService.PreWarmCacheAsync();
    
    var response = req.CreateResponse(HttpStatusCode.OK);
    await response.WriteStringAsync("Cache pre-warmed successfully");
    return response;
}
```

---

## 🧪 Post-Deployment Validation

### Test 1: Enhanced Classification

```bash
POST https://smartstudy-func.azurewebsites.net/api/answers/evaluate/v2
Content-Type: application/json

{
  "examId": 1,
  "questionId": 101,
  "studentAnswer": "F = m * a",
  "userId": 1001
}
```

**Expected**:
- Engine: `Mathematics Rule-Based Engine`
- Confidence: `> 0.8`
- Audit trail logged to `EvaluationAuditLog` table

### Test 2: Unit Validation

```bash
POST https://smartstudy-func.azurewebsites.net/api/answers/evaluate/v2
Content-Type: application/json

{
  "examId": 1,
  "questionId": 102,
  "studentAnswer": "50 cm",
  "userId": 1001
}
```

**Expected**: Convert `50 cm` → `0.5 m` and compare against model answer

### Test 3: Syllabus-Restricted Evaluation

```bash
POST https://smartstudy-func.azurewebsites.net/api/answers/evaluate/v2
Content-Type: application/json

{
  "examId": 1,
  "questionId": 103,
  "studentAnswer": "Photosynthesis is the process by which plants make food",
  "userId": 1001
}
```

**Expected**:
- Load syllabus from Blob
- Match keywords against syllabus content
- Flag if outside-syllabus content detected

### Test 4: Audit Log Verification

```sql
-- Check recent evaluations
SELECT TOP 10
    EvaluationId,
    EngineName,
    SubjectCategory,
    MarksAwarded,
    ConfidenceScore,
    NeedsReview,
    EvaluatedAt
FROM EvaluationAuditLog
ORDER BY EvaluatedAt DESC;

-- Check engine statistics
SELECT 
    EngineName,
    COUNT(*) as TotalEvaluations,
    AVG(ConfidenceScore) as AvgConfidence,
    SUM(CASE WHEN NeedsReview = 1 THEN 1 ELSE 0 END) as ReviewCount
FROM EvaluationAuditLog
GROUP BY EngineName;
```

---

## 📊 Monitoring & Alerts

### Key Metrics to Track

1. **Confidence Scores**: Alert if < 0.7 for >10% of evaluations
2. **Review Queue**: Monitor `NeedsReview = 1` count
3. **Processing Time**: Alert if > 5 seconds per evaluation
4. **Cache Hit Rate**: Monitor syllabus cache efficiency
5. **Engine Usage**: Track which engines are used most

### Application Insights Queries

```kusto
// Low confidence evaluations
customEvents
| where name == "EvaluationCompleted"
| where customDimensions.Confidence < 0.7
| summarize count() by bin(timestamp, 1h), tostring(customDimensions.Engine)

// Processing time by engine
customEvents
| where name == "EvaluationCompleted"
| summarize avg(toint(customDimensions.ProcessingTimeMs)) by tostring(customDimensions.Engine)

// Cache performance
traces
| where message contains "Syllabus cache"
| summarize HitCount = countif(message contains "HIT"), 
            MissCount = countif(message contains "MISS")
| extend HitRate = HitCount * 100.0 / (HitCount + MissCount)
```

---

## 🔧 Rollback Plan

If issues arise, rollback to V1:

### Option 1: Code-Level Rollback
```bash
# Deploy previous version
git checkout v1.0
dotnet build -c Release
func azure functionapp publish smartstudy-func
```

### Option 2: Traffic Split (Zero Downtime)
```bash
# Create staging slot
az functionapp deployment slot create \
    --name smartstudy-func \
    --resource-group rg-smartstudy-dev \
    --slot staging

# Deploy V2 to staging
func azure functionapp publish smartstudy-func --slot staging

# Test staging
curl https://smartstudy-func-staging.azurewebsites.net/api/health

# Swap when ready
az functionapp deployment slot swap \
    --name smartstudy-func \
    --resource-group rg-smartstudy-dev \
    --slot staging
```

---

## 🎯 Success Criteria

### Phase 1: Basic Functionality (Week 1)
- ✅ All 4 engines processing evaluations
- ✅ Audit logs being written
- ✅ Syllabus cache operational
- ✅ No critical errors in Application Insights

### Phase 2: Performance Validation (Week 2)
- ✅ Average processing time < 3 seconds
- ✅ Syllabus cache hit rate > 80%
- ✅ Confidence scores > 0.7 for 85%+ of evaluations

### Phase 3: Quality Assurance (Week 3)
- ✅ Teacher review queue < 15% of total evaluations
- ✅ Student feedback quality rated 4+/5
- ✅ No symbolic equivalence false positives
- ✅ Unit conversions 100% accurate

### Phase 4: Scale Testing (Week 4)
- ✅ Handle 1000+ concurrent evaluations
- ✅ Azure Function scale-out working correctly
- ✅ Database connection pooling efficient
- ✅ Blob Storage throttling not triggered

---

## 📞 Support & Escalation

### Common Issues

**Issue**: Syllabus blob not found  
**Solution**: Check Blob Storage structure, ensure files uploaded

**Issue**: Low classification confidence  
**Solution**: Review question text, add keywords to classifier

**Issue**: Unit conversion errors  
**Solution**: Check `UnitValidationHelpers` for supported units

**Issue**: Slow evaluation times  
**Solution**: Enable syllabus cache pre-warming, check database indexes

### Contact

- **Dev Team**: [email protected]
- **On-Call**: +91-XXX-XXX-XXXX
- **Azure Support**: Portal → Help + Support

---

## ✅ Final Checklist

Before marking deployment as **COMPLETE**:

- [ ] Audit log table created
- [ ] Syllabus content uploaded to Blob Storage
- [ ] DI services registered in Program.cs
- [ ] Build succeeded with 0 errors
- [ ] Deployed to Azure Function App
- [ ] Post-deployment tests passed
- [ ] Application Insights configured
- [ ] Cache pre-warmed
- [ ] Monitoring alerts configured
- [ ] Documentation updated

---

**Deployment Lead**: _____________________  
**Date Completed**: _____________________  
**Production URL**: https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net
