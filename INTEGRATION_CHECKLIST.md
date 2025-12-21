# INTEGRATION CHECKLIST - V2 Evaluation System
## SmartStudyFunc - Production Integration Steps

**Purpose**: Step-by-step checklist to integrate production-grade improvements  
**Estimated Time**: 30-45 minutes  
**Prerequisites**: Database access, Azure Portal access, local development environment

---

## ✅ Step 1: Database Migration (5 minutes)

### 1.1 Connect to Azure SQL Database
```bash
sqlcmd -S smartstudy-sql.database.windows.net \
       -d SmartStudyDB \
       -U sqladmin \
       -P <your-password>
```

### 1.2 Run Migration Script
```bash
sqlcmd -S smartstudy-sql.database.windows.net \
       -d SmartStudyDB \
       -U sqladmin \
       -P <your-password> \
       -i c:\SmartStudyFunc\sql\CreateEvaluationAuditLogTable.sql
```

### 1.3 Verify Table Creation
```sql
SELECT 
    name, 
    create_date 
FROM sys.tables 
WHERE name = 'EvaluationAuditLog';

SELECT 
    name 
FROM sys.indexes 
WHERE object_id = OBJECT_ID('EvaluationAuditLog');
```

**Expected**: 1 table + 6 indexes

☐ Database migration complete

---

## ✅ Step 2: Azure Blob Storage Setup (10 minutes)

### 2.1 Create Syllabus Container
```bash
# Azure Portal → Storage Account → Containers → + Container
# Name: syllabus
# Access level: Private
```

### 2.2 Upload Syllabus Files
**Structure**:
```
syllabus/
  ├── class-6/
  │   ├── mathematics.txt
  │   ├── physics.txt
  │   ├── chemistry.txt
  │   ├── biology.txt
  │   └── socialscience.txt
  ├── class-7/ (same files)
  ...
  └── class-12/ (same files)
```

**Sample Content** (biology.txt for Class 10):
```
Photosynthesis: Process by which plants make food using sunlight, water, and carbon dioxide.
Respiration: Process of breaking down glucose to release energy in cells.
Cell Structure: Basic unit of life containing nucleus, mitochondria, chloroplast.
...
```

### 2.3 Verify Upload
```bash
az storage blob list \
    --account-name smartstudystorage \
    --container-name syllabus \
    --output table
```

☐ Syllabus content uploaded for all 7 classes × 5 subjects = 35 files

---

## ✅ Step 3: Update Program.cs (5 minutes)

### 3.1 Add Service Registrations
Open `c:\SmartStudyFunc\Program.cs` and add after existing service registrations:

```csharp
// PRODUCTION V2 ENHANCEMENTS - Add these lines

// Enhanced Question Classifier
services.AddSingleton<EnhancedQuestionClassifier>();

// Syllabus Cache Service
services.AddSingleton<SyllabusCacheService>();

// Evaluation Audit Logger
services.AddSingleton<EvaluationAuditLogger>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<EvaluationAuditLogger>>();
    var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
                        ?? Environment.GetEnvironmentVariable("SqlConnectionString")
                        ?? throw new InvalidOperationException("SQL connection string not configured");
    return new EvaluationAuditLogger(logger, connectionString);
});

// Memory Cache for syllabus (Azure Function-friendly)
services.AddMemoryCache(options =>
{
    options.SizeLimit = 100 * 1024 * 1024; // 100 MB limit
    options.ExpirationScanFrequency = TimeSpan.FromMinutes(5);
});
```

### 3.2 Verify Compilation
```bash
cd c:\SmartStudyFunc
dotnet build
```

**Expected**: `Build succeeded. 0 Error(s)`

☐ DI configuration updated and compiles

---

## ✅ Step 4: Update Existing Engines (OPTIONAL Enhancement)

### 4.1 Option A: Keep Current Engines
- V1 engines already work
- V2 improvements are **additive** (new helper classes)
- No changes required to existing engines

☐ Using existing V1 engines as-is

### 4.2 Option B: Enhance Engines to Use V2 Helpers
Update `MathematicsEvaluationEngine.cs` to use new helpers:

```csharp
// Replace existing NormalizeExpression method with:
private string NormalizeExpression(string expression)
{
    return MathEvaluationHelpers.NormalizeExpression(expression);
}

// Replace existing CheckSymbolicEquivalence with:
private SymbolicEquivalenceResult CheckSymbolicEquivalence(string studentExpr, string modelExpr)
{
    return MathEvaluationHelpers.CheckSymbolicEquivalence(studentExpr, modelExpr);
}
```

Update `PhysicsChemistryEvaluationEngine.cs`:

```csharp
// Add to class
using static UnitValidationHelpers;

// In EvaluateNumericalWithUnitsAsync:
var result = UnitValidationHelpers.ValidateWithUnits(
    context.StudentAnswer,
    context.ModelAnswer,
    tolerancePercent: 2.0
);
```

Update `BiologySocialEvaluationEngine.cs`:

```csharp
// Add to constructor
private readonly SyllabusCacheService _syllabusCache;

// In LoadSyllabusContentAsync:
return await _syllabusCache.GetSyllabusContentAsync(
    subject,
    classLevel,
    customPath,
    cancellationToken
);
```

☐ Engines enhanced with V2 helpers (OPTIONAL)

---

## ✅ Step 5: Enable Audit Logging (Integration)

### 5.1 Update EvaluateAnswerV2.cs
Add audit logging after evaluation:

```csharp
// In EvaluateAnswerV2 function, after router.RouteAndEvaluateAsync:

var auditEntry = new EvaluationAuditEntry
{
    EvaluationId = Guid.NewGuid().ToString(),
    QuestionId = question.QuestionId.ToString(),
    ExamId = request.ExamId,
    UserId = request.UserId,
    EngineName = result.ProcessedBy,
    SubjectCategory = classification.Subject,
    QuestionType = classification.Type,
    ClassLevel = extractedClassLevel, // From question or context
    StudentAnswer = request.StudentAnswer,
    ModelAnswer = question.CorrectAnswer,
    MarksAwarded = result.MarksAwarded,
    MaxMarks = result.MaxMarks,
    ConfidenceScore = result.ConfidenceScore,
    NeedsReview = result.NeedsReview,
    EvaluationReason = result.EvaluationReason,
    MatchedKeywords = result.MatchedKeywords,
    MissingKeywords = result.MissingKeywords,
    StepWiseBreakdown = result.StepWiseBreakdown,
    AuditTrail = result.AuditTrail,
    ProcessingTimeMs = stopwatch.ElapsedMilliseconds
};

await _auditLogger.LogEvaluationAsync(auditEntry, cancellationToken);
```

### 5.2 Inject AuditLogger in Constructor
```csharp
private readonly EvaluationAuditLogger _auditLogger;

public EvaluateAnswerV2(
    ISubjectRouter router,
    ILogger<EvaluateAnswerV2> logger,
    EvaluationAuditLogger auditLogger, // Add this
    SqlDb sqlDb)
{
    _router = router;
    _logger = logger;
    _auditLogger = auditLogger; // Add this
    _sqlDb = sqlDb;
}
```

☐ Audit logging integrated

---

## ✅ Step 6: Build & Test Locally (5 minutes)

### 6.1 Clean Build
```bash
cd c:\SmartStudyFunc
dotnet clean
dotnet build -c Release
```

**Expected**: `Build succeeded. 0 Error(s), 17 Warning(s)` (warnings are nullable references - safe to ignore)

### 6.2 Start Local Function
```bash
func start
```

**Expected**: Function host started, all 18 functions listed

### 6.3 Test Enhanced Classifier
```bash
curl -X POST http://localhost:7071/api/answers/evaluate/v2 \
  -H "Content-Type: application/json" \
  -d '{
    "examId": 1,
    "questionId": 101,
    "studentAnswer": "F = m * a",
    "userId": 1001
  }'
```

**Expected**: JSON response with `ProcessedBy: "Mathematics Rule-Based Engine"`

☐ Local testing successful

---

## ✅ Step 7: Deploy to Azure (5 minutes)

### 7.1 Publish Function App
```bash
func azure functionapp publish smartstudy-func
```

**Expected**: 
```
Deployment completed successfully.
Functions in smartstudy-func:
    EvaluateAnswerV2 - [httpTrigger]
        Invoke url: https://smartstudy-func...
```

### 7.2 Verify Deployment
```bash
curl https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net/api/health
```

**Expected**: `{ "status": "healthy", "timestamp": "..." }`

☐ Deployment to Azure successful

---

## ✅ Step 8: Post-Deployment Validation (10 minutes)

### 8.1 Run Test Suite
Execute tests from `PRODUCTION_TEST_CASES.md`:

1. **Mathematics Test** (Test 1.2)
```bash
curl -X POST https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net/api/answers/evaluate/v2 \
  -H "Content-Type: application/json" \
  -d '{
    "examId": 1,
    "questionId": 102,
    "studentAnswer": "F = m * a",
    "userId": 1001
  }'
```

☐ Mathematics engine working

2. **Unit Validation Test** (Test 2.1)
```bash
curl -X POST https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net/api/answers/evaluate/v2 \
  -H "Content-Type: application/json" \
  -d '{
    "examId": 2,
    "questionId": 201,
    "studentAnswer": "50 cm",
    "userId": 1001
  }'
```

☐ Unit validation working

3. **Syllabus-Restricted Test** (Test 3.1)
```bash
curl -X POST https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net/api/answers/evaluate/v2 \
  -H "Content-Type: application/json" \
  -d '{
    "examId": 3,
    "questionId": 301,
    "studentAnswer": "Photosynthesis is the process by which plants make food using sunlight",
    "userId": 1001
  }'
```

☐ Syllabus cache and biology engine working

### 8.2 Verify Audit Logs
```sql
SELECT TOP 3
    EvaluationId,
    EngineName,
    SubjectCategory,
    MarksAwarded,
    ConfidenceScore,
    EvaluatedAt
FROM EvaluationAuditLog
ORDER BY EvaluatedAt DESC;
```

**Expected**: 3 rows from recent tests

☐ Audit logging working

### 8.3 Check Cache Performance
```bash
# Call syllabus-dependent endpoint multiple times
for i in {1..5}; do
  curl -X POST https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net/api/answers/evaluate/v2 \
    -H "Content-Type: application/json" \
    -d '{"examId": 3, "questionId": 301, "studentAnswer": "Photosynthesis", "userId": 1001}'
done
```

**Check Application Insights**:
- First call: Should see Blob Storage logs
- Subsequent calls: Should see "Syllabus cache HIT"

☐ Cache working efficiently

---

## ✅ Step 9: Monitoring Setup (5 minutes)

### 9.1 Create Application Insights Alerts
Azure Portal → Application Insights → Alerts → + New alert rule

**Alert 1: Low Confidence Rate**
```kusto
customEvents
| where name == "EvaluationCompleted"
| where customDimensions.Confidence < 0.7
| summarize count() by bin(timestamp, 1h)
| where count_ > 100
```

**Alert 2: High Processing Time**
```kusto
customEvents
| where name == "EvaluationCompleted"
| summarize avg(toint(customDimensions.ProcessingTimeMs))
| where avg_customDimensions_ProcessingTimeMs > 5000
```

☐ Monitoring alerts configured

---

## ✅ Step 10: Documentation & Handoff

### 10.1 Files to Review
- ☐ Read: `V2_IMPLEMENTATION_SUMMARY.md` (architecture overview)
- ☐ Read: `PRODUCTION_DEPLOYMENT_V2.md` (deployment guide)
- ☐ Review: `PRODUCTION_TEST_CASES.md` (testing strategy)

### 10.2 Knowledge Transfer
- ☐ Team briefing scheduled
- ☐ Teacher dashboard requirements documented
- ☐ Syllabus content ownership assigned

---

## 📊 Integration Status Dashboard

| Component | Status | Notes |
|-----------|--------|-------|
| Database Migration | ☐ | EvaluationAuditLog table |
| Syllabus Upload | ☐ | 35 files (7 classes × 5 subjects) |
| DI Configuration | ☐ | Program.cs updated |
| Engine Enhancement | ☐ | Optional - using V2 helpers |
| Audit Integration | ☐ | EvaluateAnswerV2.cs updated |
| Local Testing | ☐ | func start + tests |
| Azure Deployment | ☐ | func azure functionapp publish |
| Validation Tests | ☐ | 3 key tests passed |
| Monitoring | ☐ | Application Insights alerts |
| Documentation | ☐ | Team reviewed |

---

## 🎯 Success Criteria

### ✅ Integration Complete When:
- [x] All checkboxes above marked as complete
- [x] Build: 0 errors
- [x] Tests: 3/3 passing (Math, Unit, Syllabus)
- [x] Audit logs: Data flowing to database
- [x] Cache: Hit rate >50% after 10 calls
- [x] Monitoring: 2 alerts active in Application Insights

### ⚠️ Known Limitations (Accept as-is)
- Nullable reference warnings: **Acceptable** (C# 8 nullability)
- First evaluation slow: **Expected** (cold start + cache miss)
- Syllabus size limit: **100 KB per file** (if larger, paginate)

---

## 🚨 Rollback Procedure

If integration fails:

1. **Code Rollback**
```bash
git checkout HEAD~1
dotnet build -c Release
func azure functionapp publish smartstudy-func
```

2. **Database Rollback**
```sql
DROP TABLE IF EXISTS EvaluationAuditLog;
```

3. **DI Rollback**
Remove added services from Program.cs

4. **Verify**
```bash
curl https://smartstudy-func.../api/health
```

---

## 📞 Support Contacts

**Integration Issues**: [email protected]  
**Database Issues**: [email protected]  
**Azure Issues**: Azure Support (Portal)  

---

**Integrated By**: _____________________  
**Date**: _____________________  
**Status**: ☐ In Progress ☐ Complete ☐ Blocked  
**Next Review**: _____________________
