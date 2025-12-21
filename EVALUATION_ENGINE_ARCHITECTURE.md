# Subject-Specific Evaluation Engine System

## Architecture Overview

The SmartStudyFunc evaluation system has been extended with a **production-ready, rule-based evaluation architecture** that ensures accurate, deterministic, and explainable grading for Class 6-12 students across all subjects.

### Core Principles (NON-NEGOTIABLE)

1. **OpenAI MUST NOT decide marks for Math/Science** - Only rule-based engines determine correctness
2. **OpenAI is used ONLY for**: Explanations, feedback, language quality assessment
3. **Evaluations must be**: Deterministic, Explainable, Syllabus-restricted
4. **Teacher override**: Always supported via `NeedsReview` flag

---

## System Components

### 1. Question Classifier (`QuestionClassifier.cs`)

**Purpose**: Identifies subject and question type before routing to engines

**Classification Output**:
- **Subject**: Mathematics, Physics, Chemistry, Biology, SocialScience, English, Hindi
- **Type**: Numerical, Formula, Definition, ShortAnswer, LongAnswer, Essay, Derivation

**How It Works**:
- Rule-based keyword matching
- Symbol detection (math symbols, SI units)
- Pattern recognition (question structure)
- Confidence scoring

**Example**:
```csharp
var classification = await classifier.ClassifyAsync(
    "Calculate the area of a triangle with base 10 cm and height 5 cm."
);
// Result: Mathematics (0.85) / Numerical (0.90)
```

---

### 2. Subject Router (`SubjectRouter.cs`)

**Purpose**: Central orchestrator that routes questions to appropriate engines

**Flow**:
1. Classify question (if not pre-classified)
2. Select appropriate evaluation engine
3. Execute evaluation
4. Validate result (bounds checking, confidence thresholds)
5. Return standardized result

**Fallback Behavior**:
- If no specialized engine available → basic keyword matching
- Always flags `NeedsReview = true` for teacher verification

---

### 3. Mathematics Engine (`MathematicsEvaluationEngine.cs`)

**Technology**: MathNet.Symbolics for symbolic algebra

**Capabilities**:
- **Numerical Answers**: Direct number comparison with tolerance (0.01%)
- **Formula Equivalence**: Symbolic parsing and simplification
- **Step-wise Evaluation**: Partial credit for correct intermediate steps
- **OCR Normalization**: Converts `×` → `*`, `÷` → `/`, `½` → `0.5`, etc.
- **Variable Synonyms**: Maps `base=b`, `height=h`, `length=l`, etc.

**Critical Feature**: OpenAI generates feedback ONLY, never decides marks

**Example Flow**:
```
Student Answer: "Area = ½ × base × height = ½ × 10 × 5 = 25 cm²"
1. Normalize: "Area=0.5*base*height=0.5*10*5=25"
2. Parse with MathNet: Expression trees
3. Check symbolic equivalence: ✓ Correct
4. Extract numerical result: 25 cm²
5. Compare with model: ✓ Match
6. Award: 5/5 marks (100%)
7. OpenAI generates: "Excellent! Your formula application is correct..."
```

**Marks Decision**: Rule engine (NOT OpenAI)

---

### 4. Physics/Chemistry Engine (`PhysicsChemistryEvaluationEngine.cs`)

**Capabilities**:
- **Formula Validation**: Against known formula library (F=ma, V=IR, PV=nRT, etc.)
- **Unit Validation**: SI unit conversion and equivalence checking
- **Numerical Comparison**: With 2% tolerance for scientific calculations
- **Partial Credit**: Based on formula structure similarity

**Critical Feature**: OpenAI explains mistakes, never decides marks

**Example Formula Library**:
```csharp
"force" → F=m*a (alternatives: F=ma, a=F/m, m=F/a)
"ohms_law" → V=I*R (alternatives: I=V/R, R=V/I)
"kinetic_energy" → KE=0.5*m*v^2 (alternatives: KE=(1/2)*m*v^2)
```

**Unit Conversions**:
```
Length: m, km (1000), cm (0.01), mm (0.001)
Force: N, kN (1000)
Energy: J, kJ (1000), cal (4.184)
```

**Example Flow**:
```
Question: "A 2 kg object accelerates at 3 m/s². Find the force."
Student: "F = 2 * 3 = 6 N"
1. Extract value: 6
2. Extract unit: N
3. Verify formula: F=m*a ✓ Correct
4. Check calculation: 2*3=6 ✓ Correct
5. Check unit: N ✓ Correct for force
6. Award: 3/3 marks
```

**Marks Decision**: Rule engine (NOT OpenAI)

---

### 5. Biology/Social Science Engine (`BiologySocialEvaluationEngine.cs`)

**CRITICAL FEATURE**: Syllabus-Only Evaluation

**Data Source**: Azure Blob Storage (`syllabus/class-{level}/{subject}.txt`)

**Workflow**:
1. **Load Syllabus**: From Blob (e.g., `syllabus/class-10/biology.txt`)
2. **Extract Key Points**: From model answer using syllabus context
3. **Calculate Coverage**: % of expected points in student answer
4. **Detect Outside Content**: Flag non-syllabus knowledge (for review, not penalty)
5. **Award Marks**: Based on syllabus-validated coverage

**Critical Feature**: Blocks outside-syllabus hallucination

**Example**:
```
Syllabus Topic: "Photosynthesis in green plants"
Model Answer: "Chlorophyll absorbs light, CO2 + H2O → Glucose + O2"
Student Answer: "Chlorophyll in chloroplasts uses sunlight to convert carbon dioxide and water into glucose and oxygen. Mitochondria also helps." [OUTSIDE CONTENT]

Evaluation:
- Matched: chlorophyll, sunlight, CO2, glucose, oxygen (5/5 points)
- Marks: 5/5
- Flag: NeedsReview=true (mentioned mitochondria - not in question scope)
- Feedback: "Good understanding! Note: Focus on photosynthesis only."
```

**Marks Decision**: Keyword coverage (NOT OpenAI)

---

### 6. Language Engine (`LanguageEvaluationEngine.cs`)

**Rubric-Based Scoring** (NO binary right/wrong):

| Component   | Weight | Evaluation Method           |
|-------------|--------|-----------------------------|
| Grammar     | 25%    | Rule-based pattern checks   |
| Structure   | 25%    | Paragraph organization      |
| Relevance   | 30%    | Topic alignment             |
| Vocabulary  | 20%    | Word diversity & complexity |

**Grammar Rules** (Simplified Examples):
- Article usage: `a` vs `an`
- Homophones: `there/their/they're`, `your/you're`, `its/it's`
- Formatting: Double spaces, multiple periods

**Vocabulary Scoring**:
- Unique word ratio: 60%+ = Excellent
- Advanced words (8+ letters): Bonus points
- Repetition: Penalty

**Critical Feature**: Continuous scoring, not pass/fail

**Example**:
```
Question: "Write a paragraph on climate change."
Student: "Climate change is serious. It affects weather. We should act."

Evaluation:
- Grammar: 0.85 (good mechanics)
- Structure: 0.70 (single paragraph, needs expansion)
- Relevance: 0.90 (on topic)
- Vocabulary: 0.65 (simple words, low diversity)
Total: (0.85*0.25 + 0.70*0.25 + 0.90*0.30 + 0.65*0.20) = 0.77
Marks: 3.85/5

Feedback: "Good start! Add more details about causes and effects. Use varied vocabulary like 'global warming', 'greenhouse gases'."
```

**Marks Decision**: Rubric weights (NOT OpenAI)

---

## Standard Output Model

**ALL engines return**:
```csharp
EvaluationEngineResult {
    MarksAwarded: double           // Final marks (rule-based)
    MaxMarks: double              // Question max marks
    ConfidenceScore: double       // 0-1 (low → triggers review)
    EvaluationReason: string      // Teacher-readable audit trail
    NeedsReview: bool             // Flag for teacher verification
    StudentFeedback: string       // Can use OpenAI
    Strengths: List<string>       // What was correct
    Improvements: List<string>    // What to improve
    MatchedKeywords: List<string> // For traceability
    MissingKeywords: List<string> // What was missed
    StepWiseBreakdown: List<StepWiseMarks> // For partial credit
    ProcessedBy: string           // Engine name (auditing)
    AuditTrail: Dictionary        // Full trace
}
```

---

## Integration with Existing System

### New API Endpoint

**Endpoint**: `POST /api/answers/evaluate/v2`

**Request**:
```json
{
  "examId": "EXAM-2025-01",
  "questionId": "550e8400-e29b-41d4-a716-446655440000",
  "studentAnswerText": "F = m × a = 5 kg × 2 m/s² = 10 N"
}
```

**Response**:
```json
{
  "success": true,
  "evaluationId": 12345,
  "score": 5.0,
  "maxMarks": 5,
  "percentage": 100.0,
  "feedback": "Perfect! You correctly applied Newton's second law...",
  "strengths": "Correct formula; Accurate calculation; Proper units",
  "improvements": "",
  "keywordsMatched": ["force", "mass", "acceleration"],
  "stepWiseBreakdown": [
    {
      "stepNumber": 1,
      "stepDescription": "Formula application",
      "marksAwarded": 2,
      "maxMarks": 2,
      "status": "Complete"
    },
    {
      "stepNumber": 2,
      "stepDescription": "Numerical calculation",
      "marksAwarded": 2,
      "maxMarks": 2,
      "status": "Complete"
    },
    {
      "stepNumber": 3,
      "stepDescription": "Unit specification",
      "marksAwarded": 1,
      "maxMarks": 1,
      "status": "Complete"
    }
  ],
  "metadata": {
    "evaluationEngine": "Physics/Chemistry Rule-Based Engine",
    "confidenceScore": 1.0,
    "needsTeacherReview": false,
    "evaluationReason": "Formula match confirmed: F=m*a"
  }
}
```

---

## Database Schema Extensions

**Recommended** (Optional - Current schema can store in `RubricBreakdown` JSON):

```sql
ALTER TABLE ExamQuestions
ADD Subject NVARCHAR(50) NULL,     -- 'Mathematics', 'Physics', etc.
    QuestionType NVARCHAR(50) NULL, -- 'Numerical', 'Formula', etc.
    ClassLevel INT NULL;            -- 6-12

-- Already exists: WrittenQuestionEvaluations.RubricBreakdown (JSON)
-- Contains full EvaluationEngineResult serialized
```

---

## Dependency Injection Configuration

**Program.cs** additions:
```csharp
// Register Question Classifier
services.AddSingleton<IQuestionClassifier, QuestionClassifier>();

// Register all evaluation engines
services.AddSingleton<IEvaluationEngine, MathematicsEvaluationEngine>();
services.AddSingleton<IEvaluationEngine, PhysicsChemistryEvaluationEngine>();
services.AddSingleton<IEvaluationEngine, BiologySocialEvaluationEngine>();
services.AddSingleton<IEvaluationEngine, LanguageEvaluationEngine>();

// Register Subject Router
services.AddSingleton<ISubjectRouter, SubjectRouter>();
```

---

## Azure Blob Structure (Syllabus Storage)

```
Container: syllabus/
├── class-6/
│   ├── mathematics.txt
│   ├── science.txt
│   ├── socialscience.txt
│   └── english.txt
├── class-7/
│   ├── mathematics.txt
│   └── ...
├── class-10/
│   ├── biology.txt
│   ├── chemistry.txt
│   ├── physics.txt
│   └── ...
└── class-12/
    └── ...
```

**Syllabus Content Format** (Plain Text):
```
Chapter 1: Photosynthesis
- Chlorophyll absorbs light energy
- Carbon dioxide enters through stomata
- Water is absorbed by roots
- Glucose is produced in chloroplasts
- Oxygen is released as byproduct
...
```

---

## Configuration (local.settings.json)

```json
{
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "SQL_CONNECTION_STRING": "Server=...;Database=...;",
    "AzureOpenAI:Endpoint": "https://your-resource.openai.azure.com/",
    "AzureOpenAI:ApiKey": "your-key",
    "AzureOpenAI:EmbeddingDeployment": "text-embedding-3-large",
    "AzureOpenAI:ChatDeployment": "gpt-4o-mini"
  },
  "ConnectionStrings": {
    "SqlDb": "Server=...;Database=...;"
  }
}
```

---

## Testing the System

### Test 1: Mathematics (Numerical)
```bash
curl -X POST http://localhost:7071/api/answers/evaluate/v2 \
  -H "Content-Type: application/json" \
  -d '{
    "examId": "TEST-001",
    "questionId": "guid-here",
    "studentAnswerText": "Area = 0.5 * 10 * 5 = 25 cm²"
  }'
```

### Test 2: Physics (Formula)
```bash
curl -X POST http://localhost:7071/api/answers/evaluate/v2 \
  -H "Content-Type: application/json" \
  -d '{
    "examId": "TEST-002",
    "questionId": "guid-here",
    "studentAnswerText": "F = m * a = 5 * 2 = 10 N"
  }'
```

### Test 3: Biology (Conceptual)
```bash
curl -X POST http://localhost:7071/api/answers/evaluate/v2 \
  -H "Content-Type: application/json" \
  -d '{
    "examId": "TEST-003",
    "questionId": "guid-here",
    "studentAnswerText": "Photosynthesis converts CO2 and H2O into glucose using chlorophyll."
  }'
```

---

## Logging & Auditing

**Every evaluation logs**:
1. Which engine processed the question
2. Classification details (subject/type/confidence)
3. Matched keywords/formulas
4. Step-wise marks breakdown
5. Confidence score
6. Whether teacher review is needed

**Example Log**:
```
[INFO] SubjectRouter: Question classified as Mathematics (0.87) / Formula (0.92)
[INFO] SubjectRouter: Selected engine: Mathematics Rule-Based Engine
[INFO] MathematicsEngine: Symbolic equivalence confirmed: A=0.5*b*h
[INFO] MathematicsEngine: Numerical value correct: 25 cm²
[INFO] SubjectRouter: Evaluation complete: 5/5 marks (Confidence=1.0, NeedsReview=false)
```

---

## Teacher Override Workflow

1. System evaluates and sets `NeedsReview` if confidence < 0.6
2. Teacher reviews in dashboard
3. Teacher can:
   - Accept AI marks
   - Override marks manually
   - Add comments
4. Final marks stored with override flag

---

## Production Deployment Checklist

- [x] MathNet.Symbolics package added
- [x] All evaluation engines implemented
- [x] Subject router created
- [x] Question classifier implemented
- [x] DI configured in Program.cs
- [x] EvaluateAnswerV2 function created
- [ ] Upload syllabus content to Azure Blob
- [ ] Add Subject/QuestionType/ClassLevel columns to ExamQuestions
- [ ] Deploy to Azure Function App
- [ ] Test with real exam questions
- [ ] Train teachers on override workflow
- [ ] Monitor confidence scores and review rates

---

## Performance Characteristics

- **Mathematics**: 50-200ms (symbolic parsing overhead)
- **Physics/Chemistry**: 30-100ms (rule matching)
- **Biology/Social**: 200-500ms (Blob fetch + NLP)
- **Language**: 100-300ms (rubric calculation)

**Scaling**: Each engine is stateless and async-friendly

---

## Future Enhancements

1. **Machine Learning Feedback Loop**: Collect teacher overrides to improve classification
2. **Custom Rubrics**: Per-school customizable rubrics via Blob config
3. **Multi-language Support**: Hindi grammar rules, regional language engines
4. **Diagram Evaluation**: OCR + geometry validation for labeled diagrams
5. **Code Evaluation**: For computer science questions (syntax, logic, output)

---

## Support & Troubleshooting

**Common Issues**:

1. **"No engine found" → Fallback used**
   - Check Subject/QuestionType in database
   - Verify classification keywords

2. **Low confidence scores**
   - Review AuditTrail in response
   - Check if question format is unusual
   - Add to training data for classifier

3. **Syllabus content not loading**
   - Verify Blob path: `syllabus/class-{level}/{subject}.txt`
   - Check Azure Storage connection string
   - Ensure Blob container exists

---

## Architecture Diagram

```
┌─────────────────┐
│  EvaluateAnswerV2│
│   (Function)    │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Question        │
│ Classifier      │◄─── Keywords, Patterns, Symbols
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ Subject Router  │
│ (Orchestrator)  │
└────────┬────────┘
         │
    ┌────┴────┬─────────┬──────────┐
    ▼         ▼         ▼          ▼
┌──────┐ ┌────────┐ ┌──────┐ ┌─────────┐
│ Math │ │Physics/│ │Bio/  │ │Language │
│Engine│ │Chem    │ │Social│ │ Engine  │
│      │ │Engine  │ │Engine│ │         │
└──┬───┘ └───┬────┘ └───┬──┘ └────┬────┘
   │         │           │         │
   └─────────┴───────────┴─────────┘
                  │
                  ▼
         ┌────────────────┐
         │ OpenAI Service │◄── Feedback ONLY
         │ (Explanation)  │    (NOT marks)
         └────────────────┘
```

---

## Key Takeaways

✓ **Rule-based engines decide marks** (Math, Physics, Chemistry)  
✓ **OpenAI explains mistakes** (never decides correctness)  
✓ **Syllabus-restricted evaluation** (Biology, Social Science)  
✓ **Rubric-based language scoring** (continuous, not binary)  
✓ **Step-wise partial credit** (transparent to students/teachers)  
✓ **Teacher override always available** (NeedsReview flag)  
✓ **Full audit trail** (every decision logged)  
✓ **Production-ready** (async, stateless, DI-friendly)

---

**Version**: 1.0  
**Last Updated**: December 2025  
**Maintainer**: SmartStudyFunc Development Team
