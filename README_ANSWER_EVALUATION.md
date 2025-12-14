# 📝 Answer Sheet Evaluation API

## Mobile UI Integration Guide

---

## 🎯 Overview

This API enables students to upload handwritten answer sheets and receive **AI-powered evaluation** with:
- ✅ Step-wise marks allocation
- ✅ Expected answers for each question
- ✅ Personalized improvement feedback
- ✅ Grade and percentage calculation

---

## 📱 API Endpoints

### Base URL
```
Production: https://smartstudy-func-bgdre2hwded4cmg6.centralindia-01.azurewebsites.net/api
Local: http://localhost:7071/api
```

---

## 1️⃣ Upload Answer Sheet

**Endpoint:** `POST /answers/upload`

**Content-Type:** `multipart/form-data`

### Request Parameters

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `examId` | string | ✅ | Exam identifier |
| `questionId` | string | ✅ | Question number |
| `file` | file | ✅ | Answer sheet image/PDF |

### Supported File Types
- 📄 PDF
- 🖼️ JPG/JPEG
- 🖼️ PNG

### File Size Limit
- Maximum: **10 MB**

### Mobile Code Example (React Native)

```javascript
const uploadAnswerSheet = async (examId, questionId, imageUri) => {
  const formData = new FormData();
  formData.append('examId', examId);
  formData.append('questionId', questionId);
  formData.append('file', {
    uri: imageUri,
    type: 'image/jpeg',
    name: 'answer-sheet.jpg'
  });

  const response = await fetch(
    `${API_BASE_URL}/answers/upload`,
    {
      method: 'POST',
      body: formData,
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    }
  );
  
  return await response.json();
};
```

### Response
```json
{
  "success": true,
  "message": "Answer uploaded successfully",
  "submissionId": "abc123-def456",
  "status": "Processing"
}
```

---

## 2️⃣ Evaluate Answer (Text-based)

**Endpoint:** `POST /answers/evaluate`

**Content-Type:** `application/json`

### Request Body

```json
{
  "ExamId": 1,
  "QuestionId": 1,
  "StudentAnswerText": "The Pythagorean theorem states that..."
}
```

### Response

```json
{
  "success": true,
  "evaluationId": 123,
  "examId": 1,
  "questionId": 1,
  "score": 7,
  "maxMarks": 10,
  "percentage": 70,
  "feedback": "Good understanding of the theorem...",
  "expectedAnswer": "The Pythagorean theorem states that in a right triangle, a² + b² = c²...",
  "stepWiseMarks": [
    {
      "step": 1,
      "description": "State theorem correctly",
      "maxMarks": 2,
      "awarded": 2,
      "reason": "Correctly stated the theorem"
    },
    {
      "step": 2,
      "description": "Draw diagram with labels",
      "maxMarks": 3,
      "awarded": 2,
      "reason": "Diagram present but labels incomplete"
    }
  ],
  "strengths": [
    "Clear understanding of basic concept",
    "Correct formula mentioned"
  ],
  "improvements": [
    "Include diagram with proper labels",
    "Show step-by-step proof"
  ],
  "keywordsMatched": ["Pythagorean", "hypotenuse", "right angle"],
  "missingKeywords": ["similar triangles"]
}
```

---

## 3️⃣ Get Evaluation Results

### By Exam
**Endpoint:** `GET /evaluations/exam/{examId}`

### By Question
**Endpoint:** `GET /evaluations/exam/{examId}/question/{questionId}`

### By Evaluation ID
**Endpoint:** `GET /evaluations/{id}`

### Response

```json
{
  "success": true,
  "examId": 1,
  "totalQuestions": 5,
  "totalScore": 35,
  "totalMarks": 50,
  "percentage": 70,
  "grade": "B+",
  "evaluations": [
    {
      "questionNumber": 1,
      "score": 7,
      "maxMarks": 10,
      "feedback": "...",
      "expectedAnswer": "..."
    }
  ]
}
```

---

## 📊 Mobile UI Components

### 1. Upload Screen

```
┌─────────────────────────────┐
│  📷 Upload Answer Sheet     │
├─────────────────────────────┤
│                             │
│   ┌───────────────────┐     │
│   │                   │     │
│   │   [Camera Icon]   │     │
│   │                   │     │
│   │  Tap to capture   │     │
│   │    or upload      │     │
│   └───────────────────┘     │
│                             │
│   Exam: [Dropdown ▼]        │
│   Question: [Dropdown ▼]    │
│                             │
│   [  📤 Upload Answer  ]    │
│                             │
└─────────────────────────────┘
```

### 2. Results Screen

```
┌─────────────────────────────┐
│  📊 Evaluation Results      │
├─────────────────────────────┤
│                             │
│   Score: 35/50 (70%)        │
│   Grade: B+                 │
│   ████████░░ 70%            │
│                             │
├─────────────────────────────┤
│  Q1: Pythagorean Theorem    │
│  Score: 7/10                │
│  ██████░░░░                 │
│                             │
│  📝 Step-wise Marks:        │
│  ├─ Step 1: 2/2 ✅          │
│  ├─ Step 2: 2/3 ⚠️          │
│  └─ Step 3: 3/5 ⚠️          │
│                             │
│  💡 Expected Answer:        │
│  "The theorem states..."    │
│                             │
│  ✨ Strengths:              │
│  • Clear concept            │
│                             │
│  📈 To Improve:             │
│  • Add diagram              │
│  • Show proof steps         │
│                             │
└─────────────────────────────┘
```

---

## 🎨 UI Color Scheme

| Score Range | Color | Grade |
|-------------|-------|-------|
| 90-100% | 🟢 Green | A+ |
| 80-89% | 🟢 Light Green | A |
| 70-79% | 🟡 Yellow | B+ |
| 60-69% | 🟡 Orange | B |
| 50-59% | 🟠 Dark Orange | C |
| 40-49% | 🔴 Light Red | D |
| 0-39% | 🔴 Red | F |

---

## 📱 Mobile Implementation Tips

### 1. Camera Integration
```javascript
// Use expo-image-picker or react-native-image-picker
import * as ImagePicker from 'expo-image-picker';

const captureAnswer = async () => {
  const result = await ImagePicker.launchCameraAsync({
    mediaTypes: ImagePicker.MediaTypeOptions.Images,
    quality: 0.8,
    allowsEditing: true,
  });
  
  if (!result.canceled) {
    uploadAnswerSheet(examId, questionId, result.assets[0].uri);
  }
};
```

### 2. Progress Indicator
Show upload/evaluation progress:
```javascript
const [status, setStatus] = useState('idle');
// idle → uploading → processing → completed
```

### 3. Offline Support
- Cache evaluation results locally
- Queue uploads when offline
- Sync when connection restored

---

## ⚡ Status Codes

| Status | Meaning |
|--------|---------|
| `Uploaded` | Answer sheet received |
| `OcrProcessing` | Extracting text from image |
| `Evaluating` | AI is grading the answer |
| `Completed` | Results ready |
| `Failed` | Error occurred |

---

## 🔒 Error Handling

### Common Errors

| Code | Message | Solution |
|------|---------|----------|
| 400 | Invalid file type | Use PDF/JPG/PNG only |
| 400 | File too large | Max 10MB |
| 404 | Question not found | Check examId/questionId |
| 500 | Evaluation failed | Retry after 30 seconds |

### Mobile Error Display
```javascript
const handleError = (error) => {
  switch(error.code) {
    case 400:
      showAlert('Invalid file', 'Please upload a clear image of your answer');
      break;
    case 500:
      showRetryButton();
      break;
  }
};
```

---

## 📋 Sample Test Data

### Test Exam ID
```
EXAM-TEST-001
```

### Test Questions
| Q# | Topic | Max Marks |
|----|-------|-----------|
| 1 | Pythagorean Theorem | 10 |
| 2 | Quadratic Equations | 8 |
| 3 | Area of Circle | 5 |

---

## 🚀 Quick Start Checklist

- [ ] Configure API base URL
- [ ] Add camera permissions
- [ ] Implement file upload
- [ ] Create results display UI
- [ ] Add loading indicators
- [ ] Handle offline scenarios
- [ ] Test with sample images

---

## 📞 Support

For API issues or integration help:
- Check function logs in Azure Portal
- Verify exam questions exist in database
- Ensure valid API keys configured

---

*Last Updated: December 2025*
