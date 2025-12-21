# Evaluation Engine Quick Reference

## When to Use Which Engine?

| Subject         | Question Type    | Engine                          | Decision Logic                    |
|-----------------|------------------|---------------------------------|-----------------------------------|
| Mathematics     | Numerical        | MathematicsEvaluationEngine     | Numeric comparison (0.01% tol)    |
| Mathematics     | Formula          | MathematicsEvaluationEngine     | Symbolic equivalence (MathNet)    |
| Mathematics     | Derivation       | MathematicsEvaluationEngine     | Step-wise equivalence             |
| Physics         | Numerical        | PhysicsChemistryEvaluationEngine| Value + unit validation           |
| Physics         | Formula          | PhysicsChemistryEvaluationEngine| Formula library match             |
| Chemistry       | Formula          | PhysicsChemistryEvaluationEngine| Formula + unit check              |
| Biology         | Any              | BiologySocialEvaluationEngine   | Syllabus keyword coverage         |
| Social Science  | Any              | BiologySocialEvaluationEngine   | Syllabus keyword coverage         |
| English         | Essay/ShortAns   | LanguageEvaluationEngine        | Rubric (Grammar + Structure + ...)  |
| Hindi           | Essay/ShortAns   | LanguageEvaluationEngine        | Rubric-based                      |

---

## Key Classes & Their Roles

```
IQuestionClassifier
├─ QuestionClassifier (impl)
   └─ Analyzes question text → Returns Subject + Type

ISubjectRouter
├─ SubjectRouter (impl)
   └─ Routes to appropriate IEvaluationEngine

IEvaluationEngine (interface)
├─ MathematicsEvaluationEngine
│  └─ MathNet.Symbolics for symbolic algebra
├─ PhysicsChemistryEvaluationEngine
│  └─ Formula library + unit conversions
├─ BiologySocialEvaluationEngine
│  └─ Azure Blob syllabus + keyword matching
└─ LanguageEvaluationEngine
   └─ Rubric scoring (Grammar, Structure, Relevance, Vocabulary)
```

---

## OpenAI Usage Rules (CRITICAL)

| Component        | OpenAI Allowed?  | Purpose                          |
|------------------|------------------|----------------------------------|
| **Mathematics**  | ❌ For marks     | ✅ For feedback only             |
| **Physics/Chem** | ❌ For marks     | ✅ For explanations only         |
| **Biology**      | ❌ For marks     | ✅ For key point extraction      |
| **Language**     | ✅ For feedback  | ✅ For generating suggestions    |

**Rule**: OpenAI NEVER decides correctness for Math/Science. Only rule engines do.

---

## Standard Response Flow

```
1. EvaluateAnswerV2 receives request
2. Load question from database (QuestionText, ModelAnswer, Keywords, etc.)
3. QuestionClassifier → Classify (Subject + Type)
4. SubjectRouter → Select engine based on classification
5. IEvaluationEngine → Evaluate using rules
6. (Optional) OpenAI → Generate feedback/explanations
7. SubjectRouter → Validate result (bounds, confidence)
8. EvaluateAnswerV2 → Save to database
9. Return EvaluationEngineResult to client
```

---

## Common Code Patterns

### Using the Router
```csharp
// Inject ISubjectRouter
public MyFunction(ISubjectRouter router) { ... }

// Build context
var context = new EvaluationContext {
    QuestionId = "...",
    QuestionText = "...",
    StudentAnswer = "...",
    ModelAnswer = "...",
    MaxMarks = 10,
    Keywords = new[] { "keyword1", "keyword2" },
    Subject = SubjectCategory.Mathematics,  // or Unknown for auto-classify
    Type = QuestionType.Numerical,          // or Unknown for auto-classify
    ClassLevel = 10
};

// Evaluate
var result = await router.RouteAndEvaluateAsync(context, cancellationToken);

// Check result
if (result.NeedsReview) {
    // Flag for teacher
}
```

### Adding a New Engine

1. Create class implementing `IEvaluationEngine`
2. Implement `CanHandle(subject, type)` method
3. Implement `EvaluateAsync(context, ct)` method
4. Register in `Program.cs`:
   ```csharp
   services.AddSingleton<IEvaluationEngine, MyNewEngine>();
   ```

---

## Debugging Tips

### Check Classification
```csharp
var classifier = new QuestionClassifier(logger);
var classification = await classifier.ClassifyAsync(questionText);
Console.WriteLine($"Subject: {classification.Subject} ({classification.SubjectConfidence:F2})");
Console.WriteLine($"Type: {classification.Type} ({classification.TypeConfidence:F2})");
Console.WriteLine($"Reasoning: {classification.ReasoningTrace}");
```

### Inspect Audit Trail
```csharp
var result = await router.RouteAndEvaluateAsync(context, ct);
foreach (var (key, value) in result.AuditTrail) {
    Console.WriteLine($"{key}: {value}");
}
```

### Enable Detailed Logging
```json
// host.json
{
  "logging": {
    "logLevel": {
      "SmartStudyFunc.Services.Evaluation": "Debug"
    }
  }
}
```

---

## Configuration Checklist

### Required Settings
```json
{
  "AzureWebJobsStorage": "...",           // For Blob access
  "SQL_CONNECTION_STRING": "...",         // For database
  "AzureOpenAI:Endpoint": "...",          // For feedback generation
  "AzureOpenAI:ApiKey": "...",            // For OpenAI
  "AzureOpenAI:ChatDeployment": "gpt-4o-mini"
}
```

### Optional Settings
```json
{
  "Evaluation:ConfidenceThreshold": 0.6,  // Below this → NeedsReview
  "Evaluation:MathTolerance": 0.0001,     // Numeric comparison tolerance
  "Evaluation:PhysicsTolerance": 0.02     // 2% for science calculations
}
```

---

## Testing Shortcuts

### Mathematics (Symbolic)
```json
POST /api/answers/evaluate/v2
{
  "examId": "TEST",
  "questionId": "...",
  "studentAnswerText": "Area = 0.5 * b * h = 25"
}
```
Expected: Symbolic equivalence check → Full marks if correct

### Physics (Units)
```json
{
  "studentAnswerText": "F = 5 kg × 2 m/s² = 10 N"
}
```
Expected: Formula match + unit validation → Full marks

### Biology (Syllabus)
```json
{
  "studentAnswerText": "Photosynthesis uses chlorophyll to convert CO2 and H2O into glucose"
}
```
Expected: Keyword coverage against syllabus → Partial marks

---

## Error Handling

All engines return fallback results on errors:
```csharp
try {
    // Evaluation logic
} catch (Exception ex) {
    _logger.LogError(ex, "Evaluation failed");
    return new EvaluationEngineResult {
        MarksAwarded = 0,
        MaxMarks = context.MaxMarks,
        ConfidenceScore = 0,
        NeedsReview = true,
        EvaluationReason = $"Error: {ex.Message}"
    };
}
```

---

## Performance Optimization

### Cache Syllabus Content
```csharp
// In BiologySocialEvaluationEngine
private readonly Dictionary<string, string> _syllabusCache = new();

var cacheKey = $"{context.ClassLevel}-{context.Subject}";
if (!_syllabusCache.TryGetValue(cacheKey, out var content)) {
    content = await LoadSyllabusContentAsync(...);
    _syllabusCache[cacheKey] = content;
}
```

### Parallel Classification (If Needed)
```csharp
var classificationTask = classifier.ClassifyAsync(questionText);
var otherTask = DoSomethingElse();
await Task.WhenAll(classificationTask, otherTask);
```

---

## Common Gotchas

❌ **Don't** use OpenAI for Math/Science correctness  
✅ **Do** use rule-based engines

❌ **Don't** hardcode syllabus content  
✅ **Do** load from Azure Blob

❌ **Don't** return marks > MaxMarks  
✅ **Do** validate in router

❌ **Don't** ignore NeedsReview flag  
✅ **Do** route to teacher review queue

❌ **Don't** catch and swallow exceptions  
✅ **Do** log and return fallback result

---

## Unit Testing Template

```csharp
[Fact]
public async Task MathEngine_CorrectFormula_ReturnsFullMarks()
{
    // Arrange
    var logger = Mock.Of<ILogger<MathematicsEvaluationEngine>>();
    var openAi = Mock.Of<OpenAiService>();
    var engine = new MathematicsEvaluationEngine(logger, openAi);
    
    var context = new EvaluationContext {
        StudentAnswer = "Area = 0.5 * base * height",
        ModelAnswer = "Area = (1/2) * b * h",
        MaxMarks = 5,
        Subject = SubjectCategory.Mathematics,
        Type = QuestionType.Formula
    };
    
    // Act
    var result = await engine.EvaluateAsync(context, CancellationToken.None);
    
    // Assert
    Assert.Equal(5.0, result.MarksAwarded);
    Assert.True(result.ConfidenceScore >= 0.9);
    Assert.False(result.NeedsReview);
}
```

---

## API Response Examples

### Success (Full Marks)
```json
{
  "success": true,
  "marksAwarded": 5.0,
  "maxMarks": 5,
  "percentage": 100.0,
  "confidenceScore": 1.0,
  "needsReview": false,
  "evaluationEngine": "Mathematics Rule-Based Engine",
  "strengths": ["Correct formula", "Accurate calculation"],
  "feedback": "Excellent work! Your solution is perfect."
}
```

### Partial Credit (Step-Wise)
```json
{
  "marksAwarded": 3.5,
  "maxMarks": 5,
  "stepWiseBreakdown": [
    { "step": 1, "marks": 2.0, "status": "Complete" },
    { "step": 2, "marks": 1.5, "status": "Partial" },
    { "step": 3, "marks": 0, "status": "Missing" }
  ],
  "improvements": ["Review step 2: calculation error", "Complete step 3"]
}
```

### Needs Review (Low Confidence)
```json
{
  "marksAwarded": 4.0,
  "confidenceScore": 0.55,
  "needsReview": true,
  "evaluationReason": "Formula structure unclear - teacher verification needed"
}
```

---

## SQL Queries for Analysis

### Check Evaluation Distribution by Engine
```sql
SELECT 
    JSON_VALUE(RubricBreakdown, '$.EvaluationEngine') AS Engine,
    COUNT(*) AS EvaluationCount,
    AVG(AwardedScore) AS AvgScore,
    AVG(CAST(JSON_VALUE(RubricBreakdown, '$.ConfidenceScore') AS FLOAT)) AS AvgConfidence
FROM WrittenQuestionEvaluations
WHERE EvaluatedAt >= DATEADD(day, -7, GETUTCDATE())
GROUP BY JSON_VALUE(RubricBreakdown, '$.EvaluationEngine')
```

### Find Low Confidence Evaluations
```sql
SELECT 
    QuestionId,
    AwardedScore,
    MaxScore,
    JSON_VALUE(RubricBreakdown, '$.ConfidenceScore') AS Confidence,
    JSON_VALUE(RubricBreakdown, '$.EvaluationReason') AS Reason
FROM WrittenQuestionEvaluations
WHERE CAST(JSON_VALUE(RubricBreakdown, '$.ConfidenceScore') AS FLOAT) < 0.6
ORDER BY EvaluatedAt DESC
```

---

## Monitoring Queries

### Average Response Time by Engine
```kusto
// Application Insights
requests
| where name == "EvaluateAnswerV2"
| extend engine = tostring(customDimensions.EvaluationEngine)
| summarize 
    count=count(), 
    p50=percentile(duration, 50), 
    p95=percentile(duration, 95) 
  by engine
```

### Review Flag Rate
```kusto
customEvents
| where name == "EvaluationCompleted"
| extend needsReview = tobool(customDimensions.NeedsReview)
| summarize total=count(), flagged=countif(needsReview)
| project ReviewRate = (flagged * 100.0) / total
```

---

## Quick Links

- **Architecture**: `EVALUATION_ENGINE_ARCHITECTURE.md`
- **Implementation**: `IMPLEMENTATION_GUIDE.md`
- **Summary**: `REFACTORING_SUMMARY.md`
- **This File**: `QUICK_REFERENCE.md`

---

**Tip**: Bookmark this file for quick lookup during development!
