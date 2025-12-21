# PRODUCTION TEST CASES - V2 Evaluation System
## Comprehensive Test Suite for SmartStudyFunc

**Purpose**: Validate all evaluation engines work correctly in production  
**Environment**: Azure Function App (Live)  
**Base URL**: `https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net`

---

## 🧮 Mathematics Engine Tests

### Test 1.1: Simple Numerical Answer
```json
POST /api/answers/evaluate/v2
{
  "examId": 1,
  "questionId": 101,
  "studentAnswer": "42",
  "userId": 1001
}
```
**Expected**:
- Engine: `Mathematics Rule-Based Engine`
- Marks: Full marks if model answer is 42 ±0.01%
- Confidence: `1.0`
- No OpenAI involvement in marks decision

---

### Test 1.2: Symbolic Equivalence (F=ma)
```json
POST /api/answers/evaluate/v2
{
  "examId": 1,
  "questionId": 102,
  "studentAnswer": "F = m * a",
  "userId": 1001
}
```
**Model Answer**: `F=ma` or `a=F/m`  
**Expected**:
- MathNet.Symbolics detects equivalence
- Marks: Full marks
- Confidence: `0.95 - 1.0`
- AuditTrail shows symbolic simplification

---

### Test 1.3: Variable Aliasing
```json
POST /api/answers/evaluate/v2
{
  "examId": 1,
  "questionId": 103,
  "studentAnswer": "Area = base × height",
  "userId": 1001
}
```
**Model Answer**: `A = b * h`  
**Expected**:
- Classifier maps: `base→b`, `height→h`, `Area→A`
- Symbols normalized: `×→*`
- Marks: Full marks
- AuditTrail shows alias transformations

---

### Test 1.4: OCR Error Handling
```json
POST /api/answers/evaluate/v2
{
  "examId": 1,
  "questionId": 104,
  "studentAnswer": "π × r²",
  "userId": 1001
}
```
**Model Answer**: `pi * r^2`  
**Expected**:
- OCR normalization: `π→pi`, `²→^2`, `×→*`
- Marks: Full marks
- Confidence: `0.95+`

---

### Test 1.5: Step-Wise Partial Credit
```json
POST /api/answers/evaluate/v2
{
  "examId": 1,
  "questionId": 105,
  "studentAnswer": "Step 1: x + 5 = 10\nStep 2: x = 5",
  "userId": 1001
}
```
**Model Answer**: `x + 5 = 10; x = 10 - 5; x = 5`  
**Expected**:
- Step 1: Full marks (correct)
- Step 2: Full marks (correct)
- Total: Full marks
- StepWiseBreakdown populated

---

## ⚡ Physics/Chemistry Engine Tests

### Test 2.1: Numerical with Units
```json
POST /api/answers/evaluate/v2
{
  "examId": 2,
  "questionId": 201,
  "studentAnswer": "50 cm",
  "userId": 1001
}
```
**Model Answer**: `0.5 m`  
**Expected**:
- Unit conversion: `50 cm → 0.5 m`
- Marks: Full marks (within 2% tolerance)
- AuditTrail shows conversion

---

### Test 2.2: Force Calculation (F=ma)
```json
POST /api/answers/evaluate/v2
{
  "examId": 2,
  "questionId": 202,
  "studentAnswer": "F = 10 kg × 2 m/s² = 20 N",
  "userId": 1001
}
```
**Expected**:
- Formula match: `F=ma` detected
- Unit validation: `kg * m/s² → N` correct
- Value: 20 N correct
- Marks: Full marks

---

### Test 2.3: Unit Mismatch Detection
```json
POST /api/answers/evaluate/v2
{
  "examId": 2,
  "questionId": 203,
  "studentAnswer": "100 kg",
  "userId": 1001
}
```
**Model Answer**: `980 N` (weight)  
**Expected**:
- Unit incompatibility detected: `kg` vs `N`
- Marks: 0
- Confidence: `0.9`
- EvaluationReason: "Unit mismatch"

---

### Test 2.4: Ohm's Law (V=IR)
```json
POST /api/answers/evaluate/v2
{
  "examId": 2,
  "questionId": 204,
  "studentAnswer": "V = 5 A × 10 Ω = 50 V",
  "userId": 1001
}
```
**Expected**:
- Formula: `V=IR` matched
- Units: `A * Ω → V` valid
- Value: 50 V correct
- Marks: Full marks

---

### Test 2.5: Energy Conversion
```json
POST /api/answers/evaluate/v2
{
  "examId": 2,
  "questionId": 205,
  "studentAnswer": "4.184 kJ",
  "userId": 1001
}
```
**Model Answer**: `1000 cal` (or `1 kcal`)  
**Expected**:
- Conversion: `4.184 kJ = 1000 cal`
- Marks: Full marks
- AuditTrail shows unit conversions

---

## 🌱 Biology/Social Science Engine Tests

### Test 3.1: Syllabus-Restricted (Biology)
```json
POST /api/answers/evaluate/v2
{
  "examId": 3,
  "questionId": 301,
  "studentAnswer": "Photosynthesis is the process by which plants make food using sunlight, water, and carbon dioxide.",
  "userId": 1001
}
```
**Expected**:
- Load syllabus from: `syllabus/class-10/biology.txt`
- Match keywords: `photosynthesis`, `sunlight`, `water`, `carbon dioxide`
- Marks: Proportional to keyword coverage
- No outside-syllabus content detected

---

### Test 3.2: Outside-Syllabus Detection
```json
POST /api/answers/evaluate/v2
{
  "examId": 3,
  "questionId": 302,
  "studentAnswer": "DNA is structured as a double helix discovered by Watson and Crick using X-ray crystallography by Rosalind Franklin.",
  "userId": 1001
}
```
**Syllabus**: Contains "DNA", "double helix" but NOT "Watson and Crick" or "X-ray crystallography"  
**Expected**:
- Matched: `DNA`, `double helix`
- Outside-syllabus: `Watson and Crick`, `X-ray crystallography`
- NeedsReview: `true`
- AuditTrail flags outside content

---

### Test 3.3: Key Point Coverage
```json
POST /api/answers/evaluate/v2
{
  "examId": 3,
  "questionId": 303,
  "studentAnswer": "Functions of roots: Absorb water and minerals",
  "userId": 1001
}
```
**Model Answer**: "Functions of roots: 1) Absorb water and minerals 2) Anchor plant 3) Store food"  
**Expected**:
- Matched: 1 of 3 key points
- Marks: ~33% of max marks
- MissingKeywords: `["anchor plant", "store food"]`
- Feedback suggests missing points

---

### Test 3.4: Social Science - Definition
```json
POST /api/answers/evaluate/v2
{
  "examId": 3,
  "questionId": 304,
  "studentAnswer": "Democracy is a form of government where power is held by the people through elected representatives.",
  "userId": 1001
}
```
**Expected**:
- Syllabus match: `democracy`, `government`, `elected representatives`
- Marks: High (good coverage)
- Confidence: `0.85+`

---

## 📝 Language Engine Tests

### Test 4.1: Essay Evaluation (English)
```json
POST /api/answers/evaluate/v2
{
  "examId": 4,
  "questionId": 401,
  "studentAnswer": "My best friend is kind, helpful, and always supports me. We enjoy playing together and sharing our thoughts. True friendship is about trust and understanding.",
  "userId": 1001
}
```
**Expected**:
- Grammar: 25% (rule-based checks)
- Structure: 25% (3 clear sentences, good flow)
- Relevance: 30% (on-topic about friendship)
- Vocabulary: 20% (diverse words: kind, helpful, supports, trust)
- Total: 85-95% marks
- No binary correctness - continuous scoring

---

### Test 4.2: Grammar Issues Detection
```json
POST /api/answers/evaluate/v2
{
  "examId": 4,
  "questionId": 402,
  "studentAnswer": "She go to school everyday. We plays cricket.",
  "userId": 1001
}
```
**Expected**:
- Grammar score: Low (subject-verb disagreement)
- Improvements: `["Check subject-verb agreement", "Review verb forms"]`
- Marks deducted from grammar component
- Feedback explains errors

---

### Test 4.3: Hindi Essay (Devanagari)
```json
POST /api/answers/evaluate/v2
{
  "examId": 4,
  "questionId": 403,
  "studentAnswer": "मेरा सबसे अच्छा दोस्त बहुत दयालु है। हम साथ में खेलते हैं और पढ़ते हैं।",
  "userId": 1001
}
```
**Expected**:
- Language Engine handles Unicode
- Rubric-based scoring still applies
- Vocabulary diversity analyzed
- Marks awarded based on rubric weights

---

## 🔄 Edge Cases & Error Handling

### Test 5.1: Empty Answer
```json
POST /api/answers/evaluate/v2
{
  "examId": 5,
  "questionId": 501,
  "studentAnswer": "",
  "userId": 1001
}
```
**Expected**:
- Marks: 0
- NeedsReview: `true`
- Confidence: `0.3`
- EvaluationReason: "Empty answer"

---

### Test 5.2: Ambiguous Question (Classification Fallback)
```json
POST /api/answers/evaluate/v2
{
  "examId": 5,
  "questionId": 502,
  "studentAnswer": "The answer is 42",
  "userId": 1001
}
```
**Question Text**: "Explain the concept."  
**Expected**:
- Classifier confidence: Low
- SubjectRouter applies fallback
- NeedsReview: `true`
- Teacher review recommended

---

### Test 5.3: Unparseable Expression
```json
POST /api/answers/evaluate/v2
{
  "examId": 5,
  "questionId": 503,
  "studentAnswer": "F = !@#$%",
  "userId": 1001
}
```
**Expected**:
- MathNet parsing fails
- Fallback to keyword matching
- Marks: 0 or partial (if any valid keywords)
- NeedsReview: `true`
- AuditTrail shows parsing error

---

### Test 5.4: Very Long Answer
```json
POST /api/answers/evaluate/v2
{
  "examId": 5,
  "questionId": 504,
  "studentAnswer": "<2000 word essay>",
  "userId": 1001
}
```
**Expected**:
- Processing time < 5 seconds
- Language engine handles gracefully
- All rubric components evaluated
- No timeout errors

---

## 📊 Audit Log Verification

After running tests, verify audit logs:

```sql
-- Check test evaluations
SELECT 
    EvaluationId,
    QuestionId,
    EngineName,
    SubjectCategory,
    MarksAwarded,
    MaxMarks,
    ConfidenceScore,
    NeedsReview,
    ProcessingTimeMs,
    EvaluatedAt
FROM EvaluationAuditLog
WHERE UserId = 1001
ORDER BY EvaluatedAt DESC;

-- Verify engine distribution
SELECT 
    EngineName,
    COUNT(*) as TestCount,
    AVG(ConfidenceScore) as AvgConfidence,
    AVG(ProcessingTimeMs) as AvgTime
FROM EvaluationAuditLog
WHERE UserId = 1001
GROUP BY EngineName;

-- Check high-confidence evaluations
SELECT COUNT(*) as HighConfidenceCount
FROM EvaluationAuditLog
WHERE UserId = 1001
  AND ConfidenceScore >= 0.8;

-- Check review queue
SELECT QuestionId, EvaluationReason
FROM EvaluationAuditLog
WHERE UserId = 1001
  AND NeedsReview = 1;
```

---

## ✅ Success Criteria

### All Tests Pass If:
- ✅ Mathematics engine: Symbolic equivalence works, no OpenAI marks
- ✅ Physics/Chemistry engine: Unit conversions accurate, formula matching works
- ✅ Biology/Social engine: Syllabus-restricted, no hallucination
- ✅ Language engine: Rubric-based scoring, no binary correctness
- ✅ Audit logs: All evaluations recorded with full trail
- ✅ Performance: All evaluations complete < 5 seconds
- ✅ Confidence: 85%+ evaluations have confidence > 0.7

### Quality Gates:
- ❌ FAIL: Any OpenAI-decided marks for Math/Science
- ❌ FAIL: Outside-syllabus content accepted without flag
- ❌ FAIL: Unit mismatch not detected
- ❌ FAIL: Audit trail incomplete
- ❌ FAIL: Processing time > 10 seconds

---

## 🎯 Test Execution

Run tests in order:
1. Mathematics (Tests 1.1 - 1.5)
2. Physics/Chemistry (Tests 2.1 - 2.5)
3. Biology/Social (Tests 3.1 - 3.4)
4. Language (Tests 4.1 - 4.3)
5. Edge Cases (Tests 5.1 - 5.4)
6. Audit Log Verification

**Total Tests**: 19  
**Expected Pass Rate**: 100%  
**Estimated Time**: 10 minutes

---

**Tester**: _____________________  
**Date**: _____________________  
**Result**: ☐ PASS ☐ FAIL  
**Notes**: _____________________
