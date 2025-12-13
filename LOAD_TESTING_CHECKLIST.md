# Load Testing Checklist - SmartStudyFunc

**Date:** November 25, 2025  
**Purpose:** Comprehensive load testing guide for validating stability improvements under heavy concurrent load

---

## Pre-Testing Preparation

### 1. Environment Setup ✅

**Azure Resources Verification:**
- [ ] Azure Function App is running (Consumption or Premium plan)
- [ ] Azure Storage Account is accessible (studyaistorage345)
- [ ] Azure SQL Database is online (school-chatbot-sql-10271900)
- [ ] Azure OpenAI Service is available (smartstudyai.openai.azure.com)
- [ ] Application Insights is configured for monitoring

**Configuration Verification:**
```powershell
# Check Function App settings
az functionapp config appsettings list --name <your-function-app> --resource-group <your-rg>

# Verify critical settings:
# - AzureWebJobsStorage
# - SqlConnectionString  
# - AzureOpenAI:Endpoint
# - AzureOpenAI:ApiKey
# - AzureOpenAI:EmbeddingDeployment (text-embedding-3-small)
# - AzureOpenAI:ChatDeployment (gpt-4o-mini)
```

**Baseline Metrics (Record Before Testing):**
- [ ] Current CPU usage: ______%
- [ ] Current memory usage: ______ MB
- [ ] SQL DTU usage: ______%
- [ ] Storage account usage: ______ GB
- [ ] OpenAI TPM quota available: ______
- [ ] Number of existing blobs in queue: ______

### 2. Test Data Preparation ✅

**PDF Test Files (Prepare 20+ PDFs):**
- [ ] **Small PDFs** (5-10 pages, ~1 MB): Quantity: 5
- [ ] **Medium PDFs** (20-50 pages, ~5 MB): Quantity: 10
- [ ] **Large PDFs** (100+ pages, ~15 MB): Quantity: 3
- [ ] **Extra Large PDFs** (200+ pages, ~30 MB): Quantity: 2

**Special Test Cases:**
- [ ] Corrupted PDF (intentionally damaged file)
- [ ] Password-protected PDF
- [ ] Image-only PDF (no text)
- [ ] PDF with special characters in filename

**File Naming Convention:**
```
test-small-01.pdf
test-medium-05.pdf
test-large-02.pdf
test-corrupted.pdf
test-protected.pdf
```

### 3. Monitoring Tools Setup ✅

**Azure Portal - Metrics to Monitor:**
- [ ] Function App → Metrics → Execution Count
- [ ] Function App → Metrics → Execution Units
- [ ] Function App → Metrics → Memory Working Set
- [ ] SQL Database → Metrics → DTU Percentage
- [ ] SQL Database → Metrics → Connection Count
- [ ] Storage Account → Metrics → Transactions
- [ ] Application Insights → Live Metrics

**Log Analytics Queries (Prepare These):**
```kusto
// Function execution times
requests
| where cloud_RoleName == "ProcessBlobFile"
| summarize 
    Count = count(),
    AvgDuration = avg(duration),
    P50 = percentile(duration, 50),
    P95 = percentile(duration, 95),
    P99 = percentile(duration, 99)
    by bin(timestamp, 5m)
| order by timestamp desc

// Error rate
exceptions
| where cloud_RoleName startswith "SmartStudyFunc"
| summarize ErrorCount = count() by operation_Name, problemId
| order by ErrorCount desc

// SQL retry attempts
traces
| where message contains "SQL error on attempt"
| summarize RetryCount = count() by operation_Name
| order by RetryCount desc

// OpenAI rate limits
traces
| where message contains "429" or message contains "rate limit"
| summarize Count = count() by bin(timestamp, 1m)
| order by timestamp desc

// Chunk processing metrics
traces
| where message contains "Chunk processing complete"
| parse message with * "processed: " Processed:int " processed, " Failed:int " failed, " Total:int " total"
| project timestamp, Processed, Failed, Total, SuccessRate = (Processed * 100.0 / Total)
| order by timestamp desc
```

---

## Test Scenarios

### Scenario 1: Baseline - Single PDF Upload ✅

**Objective:** Verify basic functionality works correctly

**Steps:**
1. Upload a single small PDF (10 pages) via HTTP endpoint:
   ```powershell
   # Using PowerShell
   $pdfPath = "C:\TestPDFs\test-small-01.pdf"
   $className = "PUC-II"
   $subject = "Mathematics"
   $chapter = "Chapter-1"
   
   $uri = "https://<your-function-app>.azurewebsites.net/api/upload/textbook"
   
   # Create multipart form data
   $boundary = [System.Guid]::NewGuid().ToString()
   $headers = @{
       "Content-Type" = "multipart/form-data; boundary=$boundary"
   }
   
   # Build form data (see UploadTextbook.cs for format)
   # ... (use Invoke-RestMethod or curl)
   ```

2. Monitor logs in real-time:
   ```powershell
   func azure functionapp logstream <your-function-app>
   ```

3. Verify blob trigger fires and processes the file

**Expected Results:**
- ✅ File uploaded successfully (HTTP 200)
- ✅ Blob trigger activates within 30 seconds
- ✅ ProcessBlobFile logs show:
  - "NEW FILE UPLOADED TO BLOB"
  - "Processing textbook: Size=X, Ext=.pdf"
  - "Extracted X chars"
  - "Chunk count: X"
  - "Processing batch 1/Y"
  - "TEXTBOOK PROCESSING COMPLETE → SUCCESS"
- ✅ Processing completes in < 5 minutes
- ✅ No errors in logs
- ✅ SQL tables populated:
  - `UploadedFiles`: New row with file metadata
  - `FileChunks`: Multiple rows (1 per chunk)
  - `ChunkEmbeddings`: Matching embedding records

**Success Criteria:**
- [x] HTTP upload returns 200 OK
- [x] Processing completes without errors
- [x] All chunks processed successfully
- [x] Database contains correct records
- [x] No exceptions thrown

---

### Scenario 2: Controlled Concurrency - 5 PDFs Simultaneously ✅

**Objective:** Test moderate concurrent load

**Steps:**
1. Prepare 5 small-to-medium PDFs (mix of sizes)
2. Upload all 5 PDFs within 60 seconds:
   ```powershell
   # Upload script
   $pdfs = @(
       "test-small-01.pdf",
       "test-small-02.pdf",
       "test-medium-01.pdf",
       "test-medium-02.pdf",
       "test-small-03.pdf"
   )
   
   foreach ($pdf in $pdfs) {
       # Upload each PDF (asynchronously if possible)
       Start-Job -ScriptBlock {
           # Upload code here
       }
   }
   ```

3. Monitor:
   - Application Insights Live Metrics
   - SQL Database DTU usage
   - Function execution times

**Expected Results:**
- ✅ With `maxDegreeOfParallelism: 1`, PDFs process sequentially
- ✅ Each PDF completes successfully
- ✅ Total processing time: ~15-25 minutes (5 PDFs × 3-5 min avg)
- ✅ No rate limit errors (429) from Azure OpenAI
- ✅ No SQL connection errors
- ✅ Memory usage stays below 500 MB per instance

**Monitoring Points:**
- [ ] Check logs for "Processing batch X/Y" messages
- [ ] Verify 4-second delays between batches are working
- [ ] Monitor SQL connection count (should stay < 10)
- [ ] Check for any retry attempts in logs
- [ ] Verify all 5 PDFs show "PROCESSING COMPLETE → SUCCESS"

**Success Criteria:**
- [x] All 5 PDFs process successfully
- [x] No unhandled exceptions
- [x] No SQL errors (or all handled via retry)
- [x] No OpenAI 429 errors (or all handled via retry)
- [x] Total processing time reasonable (< 30 minutes)
- [x] Success rate: 100% (5/5 PDFs)

---

### Scenario 3: Heavy Load - 10 PDFs Concurrently ✅

**Objective:** Test system under significant concurrent load

**Steps:**
1. Prepare 10 PDFs (mix of sizes: 5 small, 3 medium, 2 large)
2. Upload all 10 PDFs within 2 minutes
3. Enable detailed monitoring

**Expected Behavior with Current Configuration:**
- `maxDegreeOfParallelism: 1` → Blob trigger processes 1 PDF at a time
- Remaining 9 PDFs queue in Azure Storage (blob queue)
- Each PDF processes sequentially
- Total time: ~30-60 minutes (10 PDFs × 3-6 min avg)

**Monitoring Dashboard:**
```kusto
// Function execution timeline
requests
| where name == "ProcessBlobFile"
| extend PDFName = tostring(customDimensions.FileName)
| project timestamp, PDFName, duration, resultCode
| order by timestamp asc
```

**Expected Results:**
- ✅ All 10 PDFs eventually process (no crashes)
- ✅ Processing happens sequentially (due to maxDegreeOfParallelism: 1)
- ✅ Some SQL retries may occur (expected under load)
- ✅ Some OpenAI retries may occur (rate limiting)
- ✅ No complete failures (all retries succeed)
- ✅ Memory usage controlled (1 PDF at a time)

**Red Flags to Watch For:**
- ❌ Function app crashes/restarts
- ❌ Unhandled exceptions in logs
- ❌ SQL connection pool exhaustion
- ❌ OpenAI 429 errors that fail after all retries
- ❌ Stuck/zombie processes (PDFs that never complete)

**Success Criteria:**
- [x] At least 95% success rate (9-10 PDFs processed)
- [x] All errors are logged with graceful handling
- [x] No function app restarts
- [x] SQL retries succeed (no permanent failures)
- [x] OpenAI retries succeed (no permanent failures)
- [x] Total time < 90 minutes

---

### Scenario 4: Stress Test - 20 PDFs + Error Conditions ✅

**Objective:** Maximum load test with intentional error conditions

**Steps:**
1. Prepare 20 PDFs:
   - 8 small PDFs
   - 8 medium PDFs
   - 3 large PDFs
   - 1 corrupted PDF (should fail gracefully)

2. Upload all 20 PDFs within 5 minutes

3. Monitor system behavior over 2-3 hours

**Expected Processing Pattern:**
- Sequential processing (1 at a time)
- ~3-6 minutes per PDF average
- Total time: ~60-120 minutes for all 20
- Corrupted PDF logs error but doesn't crash

**Monitoring Checklist:**
- [ ] Check for memory leaks (memory should be stable over time)
- [ ] Monitor SQL connection count (should not exceed pool size)
- [ ] Check Application Insights for patterns
- [ ] Verify retry logic is working (SQL + OpenAI)
- [ ] Confirm corrupted PDF fails gracefully

**Expected Results:**
- ✅ 19/20 PDFs process successfully (corrupted one fails gracefully)
- ✅ Success rate: 95% (19/20)
- ✅ Corrupted PDF logs:
  - "Failed to extract text from PDF"
  - "File might be corrupted or password-protected"
  - Function exits gracefully (no throw)
- ✅ No cascading failures
- ✅ System remains stable throughout
- ✅ All retry attempts logged

**Performance Metrics:**
| Metric | Target | Actual |
|--------|--------|--------|
| Success Rate | ≥ 95% | ___% |
| Avg Processing Time | < 6 min | ___ min |
| P95 Processing Time | < 12 min | ___ min |
| Max Memory Usage | < 800 MB | ___ MB |
| SQL Connection Errors | 0 | ___ |
| OpenAI 429 Errors (unhandled) | 0 | ___ |
| Function Crashes | 0 | ___ |

**Success Criteria:**
- [x] ≥ 95% success rate (at least 19/20 PDFs)
- [x] Corrupted PDF handled gracefully (no crash)
- [x] No function app crashes or restarts
- [x] No unhandled exceptions in production logs
- [x] Memory usage stable (no leaks)
- [x] All transient errors retry successfully

---

### Scenario 5: Edge Cases & Failure Modes ✅

**Objective:** Test specific failure scenarios

#### Test 5.1: Password-Protected PDF
- [ ] Upload password-protected PDF
- [ ] Expected: "Failed to extract text" logged, function exits gracefully
- [ ] Verify: No crash, next PDF processes normally

#### Test 5.2: Image-Only PDF (No Text)
- [ ] Upload PDF with only images (scanned document)
- [ ] Expected: "No text extracted" logged, function exits gracefully
- [ ] Verify: 0 chunks created, no errors

#### Test 5.3: Extremely Large PDF (200+ pages)
- [ ] Upload 200+ page PDF
- [ ] Expected: Processing takes 20-30 minutes
- [ ] Verify: Function timeout (30 min) is sufficient
- [ ] Check: Memory usage stays under control

#### Test 5.4: Special Characters in Filename
- [ ] Upload PDF with name: `test-😀-special!@#$.pdf`
- [ ] Expected: Sanitized to safe blob name
- [ ] Verify: `SanitizeBlobName` function works correctly

#### Test 5.5: Concurrent Uploads to Same Path
- [ ] Upload 3 PDFs with same class/subject/chapter simultaneously
- [ ] Expected: All 3 process successfully
- [ ] Verify: No naming conflicts, all stored correctly

#### Test 5.6: Database Unavailable (Simulated)
- [ ] Temporarily revoke SQL access (or use invalid connection string)
- [ ] Upload 1 PDF
- [ ] Expected: SQL retry logic attempts 3 times, then logs error
- [ ] Verify: Function logs "Failed to insert file metadata after 3 attempts"
- [ ] Restore access and verify next PDF works

#### Test 5.7: OpenAI Service Throttling (Simulated)
- [ ] Upload 10+ PDFs rapidly to trigger rate limiting
- [ ] Expected: Some 429 errors, but all retry successfully
- [ ] Verify: Logs show "Transient error on attempt X/3" messages
- [ ] Check: No permanent failures

**Success Criteria for Edge Cases:**
- [x] All edge cases handled gracefully (no crashes)
- [x] Appropriate error messages logged for each scenario
- [x] System recovers and processes next file successfully
- [x] No data corruption in database

---

## Monitoring & Alerting Setup

### Key Metrics to Track

**Function Execution Metrics:**
```kusto
// Success rate over time
requests
| where name == "ProcessBlobFile"
| summarize 
    Total = count(),
    Success = countif(success == true),
    Failed = countif(success == false),
    SuccessRate = (countif(success == true) * 100.0 / count())
    by bin(timestamp, 15m)
| order by timestamp desc
```

**Error Patterns:**
```kusto
// Top errors by frequency
exceptions
| where cloud_RoleName startswith "SmartStudyFunc"
| summarize Count = count() by type, outerMessage
| order by Count desc
| take 10
```

**Performance Trends:**
```kusto
// Processing time trends
requests
| where name == "ProcessBlobFile"
| summarize 
    P50 = percentile(duration, 50),
    P95 = percentile(duration, 95),
    Max = max(duration)
    by bin(timestamp, 1h)
| order by timestamp desc
```

### Alerts to Configure

**Critical Alerts (Immediate Action Required):**
1. **Function Execution Failure Rate > 10%**
   - Threshold: More than 10% of executions fail in 15 minutes
   - Action: Check logs immediately

2. **SQL Connection Errors**
   - Threshold: Any SQL connection error that fails after retries
   - Action: Check database availability and connection pool

3. **Function App Restart/Crash**
   - Threshold: Any unplanned restart
   - Action: Investigate immediately

**Warning Alerts (Monitor Closely):**
1. **High Processing Time**
   - Threshold: P95 > 15 minutes
   - Action: Check for large PDFs or performance degradation

2. **OpenAI Rate Limiting**
   - Threshold: > 10 rate limit errors in 5 minutes
   - Action: Consider increasing OpenAI quota

3. **Memory Usage High**
   - Threshold: > 700 MB per instance
   - Action: Check for memory leaks

---

## Post-Test Analysis

### Data Collection Checklist

After completing all test scenarios, collect:

- [ ] **Success Metrics:**
  - Total PDFs uploaded: ______
  - Successfully processed: ______
  - Failed (gracefully): ______
  - Success rate: ______%

- [ ] **Performance Metrics:**
  - Average processing time: ______ minutes
  - P50 processing time: ______ minutes
  - P95 processing time: ______ minutes
  - P99 processing time: ______ minutes

- [ ] **Reliability Metrics:**
  - SQL retry attempts: ______
  - SQL retries succeeded: ______
  - OpenAI retry attempts: ______
  - OpenAI retries succeeded: ______
  - Function crashes: ______ (should be 0)

- [ ] **Resource Usage:**
  - Peak memory usage: ______ MB
  - Peak SQL DTU: ______%
  - Peak CPU usage: ______%
  - Total storage used: ______ GB

### Results Analysis

**Compare Against Targets:**

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Success Rate | ≥ 95% | ___% | ⬜ Pass / ⬜ Fail |
| Avg Processing Time | < 6 min | ___ min | ⬜ Pass / ⬜ Fail |
| Function Crashes | 0 | ___ | ⬜ Pass / ⬜ Fail |
| SQL Errors (unhandled) | 0 | ___ | ⬜ Pass / ⬜ Fail |
| OpenAI Errors (unhandled) | 0 | ___ | ⬜ Pass / ⬜ Fail |
| Memory Leaks | 0 | ___ | ⬜ Pass / ⬜ Fail |

### Issues Found

**Log any issues discovered during testing:**

| Issue # | Severity | Description | Reproduction Steps | Status |
|---------|----------|-------------|-------------------|--------|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |

---

## Rollback Procedures

### If Critical Issues Found

**Immediate Rollback Steps:**

1. **Stop Blob Processing:**
   ```powershell
   # Update host.json (or via portal)
   # Set maxDegreeOfParallelism to 0 to pause processing
   ```

2. **Restore Previous Configuration:**
   ```powershell
   # Deploy previous version
   func azure functionapp publish <your-function-app> --slot staging
   
   # After verification, swap slots
   az functionapp deployment slot swap --name <your-function-app> --resource-group <your-rg> --slot staging
   ```

3. **Clear Blob Queue (if needed):**
   ```powershell
   # If blob queue is stuck, may need to manually process or clear
   ```

4. **Verify Rollback:**
   - [ ] Previous version deployed
   - [ ] Function app running normally
   - [ ] No active errors in logs
   - [ ] Monitor for 30 minutes to ensure stability

---

## Go/No-Go Decision

**Production Deployment Approval:**

Based on test results, mark the decision:

⬜ **GO** - All tests passed, ready for production
⬜ **NO-GO** - Critical issues found, needs more work
⬜ **GO WITH CAUTION** - Minor issues found, monitor closely in production

**Sign-off:**
- Testing completed by: ________________
- Date: ________________
- Approved by: ________________
- Date: ________________

**Notes:**
_________________________________________
_________________________________________
_________________________________________

---

## Continuous Monitoring (Post-Deployment)

### First 24 Hours
- [ ] Monitor Application Insights every 2 hours
- [ ] Check error rate trends
- [ ] Verify success rate ≥ 95%
- [ ] Review any alerts triggered

### First Week
- [ ] Daily review of error logs
- [ ] Track processing time trends
- [ ] Monitor resource usage patterns
- [ ] Review retry success rates

### Ongoing
- [ ] Weekly performance review
- [ ] Monthly capacity planning
- [ ] Quarterly load test rerun
- [ ] Update this checklist based on learnings

---

## Appendix: Useful Commands

### Azure CLI Commands
```powershell
# View function logs in real-time
func azure functionapp logstream <your-function-app>

# Check function app status
az functionapp show --name <your-function-app> --resource-group <your-rg>

# Scale out (if needed)
az functionapp plan update --name <your-plan> --resource-group <your-rg> --sku P1V2

# View SQL metrics
az sql db show-usage --name <your-db> --server <your-server> --resource-group <your-rg>
```

### PowerShell Test Scripts
```powershell
# Upload multiple PDFs script
$pdfs = Get-ChildItem "C:\TestPDFs\*.pdf"
foreach ($pdf in $pdfs) {
    Write-Host "Uploading $($pdf.Name)..."
    # Upload code here
    Start-Sleep -Seconds 2  # Throttle uploads
}

# Monitor processing progress
while ($true) {
    # Query Application Insights or logs
    Start-Sleep -Seconds 30
}
```

---

**Document Version:** 1.0  
**Last Updated:** November 25, 2025  
**Next Review:** After production deployment
