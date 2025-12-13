#!/bin/bash
# ================================================================
# SmartStudy AI Evaluation System - cURL Examples
# ================================================================
# BASE URL: Replace with your Azure Function App URL
# ================================================================

BASE_URL="https://your-function-app.azurewebsites.net/api"
FUNCTION_KEY="your-function-key-here"

# ================================================================
# 1. UPLOAD ANSWER (with OCR extraction)
# ================================================================
echo "=== 1. Upload Answer with OCR ==="

curl -X POST "$BASE_URL/answers/upload?code=$FUNCTION_KEY" \
  -F "examId=101" \
  -F "questionId=5" \
  -F "file=@/path/to/student_answer.pdf" \
  -H "Content-Type: multipart/form-data"

echo ""

# Save response to variable for next step
UPLOAD_RESPONSE=$(curl -s -X POST "$BASE_URL/answers/upload?code=$FUNCTION_KEY" \
  -F "examId=101" \
  -F "questionId=5" \
  -F "file=@/path/to/student_answer.pdf")

EXTRACTED_TEXT=$(echo $UPLOAD_RESPONSE | jq -r '.extractedText')
BLOB_PATH=$(echo $UPLOAD_RESPONSE | jq -r '.blobPath')

echo "Extracted Text: $EXTRACTED_TEXT"
echo "Blob Path: $BLOB_PATH"
echo ""

# ================================================================
# 2. EVALUATE ANSWER (AI Scoring)
# ================================================================
echo "=== 2. Evaluate Answer with AI ==="

curl -X POST "$BASE_URL/answers/evaluate?code=$FUNCTION_KEY" \
  -H "Content-Type: application/json" \
  -d "{
    \"examId\": 101,
    \"questionId\": 5,
    \"studentAnswerText\": \"$EXTRACTED_TEXT\",
    \"blobPath\": \"$BLOB_PATH\"
  }" | jq .

echo ""

# ================================================================
# 3. BATCH EVALUATE (Multiple Answers)
# ================================================================
echo "=== 3. Batch Evaluate (3 answers) ==="

curl -X POST "$BASE_URL/answers/evaluate/batch?code=$FUNCTION_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "evaluations": [
      {
        "examId": 101,
        "questionId": 1,
        "studentAnswerText": "The Pythagorean theorem states that a² + b² = c² where c is the hypotenuse."
      },
      {
        "examId": 101,
        "questionId": 2,
        "studentAnswerText": "Differentiation is the process of finding the rate of change. d/dx(x²) = 2x"
      },
      {
        "examId": 101,
        "questionId": 3,
        "studentAnswerText": "The area of a circle is πr² where r is the radius of the circle."
      }
    ]
  }' | jq .

echo ""

# ================================================================
# 4. EXAMPLE RESPONSES
# ================================================================
echo "=== 4. Example Responses ==="

cat <<EOF

### UPLOAD RESPONSE ###
{
  "success": true,
  "examId": 101,
  "questionId": 5,
  "extractedText": "The derivative of x^2 is 2x. This is found using the power rule...",
  "extractedLength": 156,
  "blobPath": "answers/101/5/20250102153045.pdf",
  "fileName": "student_answer.pdf",
  "fileSize": 245678
}

### EVALUATE RESPONSE ###
{
  "success": true,
  "evaluationId": 1234,
  "examId": 101,
  "questionId": 5,
  "score": 7.5,
  "maxMarks": 10,
  "percentage": 75.0,
  "feedback": "Good understanding of differentiation. The power rule is correctly applied.",
  "strengths": "Correct formula; Clear working",
  "improvements": "Include more examples and explain the chain rule application",
  "keywordsMatched": ["derivative", "power rule", "differentiation"],
  "missingKeywords": ["chain rule"],
  "usedFallback": false
}

### BATCH EVALUATE RESPONSE ###
{
  "success": true,
  "totalRequested": 3,
  "totalProcessed": 3,
  "results": [
    {
      "success": true,
      "evaluationId": 1235,
      "questionId": 1,
      "score": 9.0,
      "maxMarks": 10,
      "percentage": 90.0,
      "feedback": "Excellent answer. Pythagorean theorem correctly stated.",
      "usedFallback": false
    },
    {
      "success": true,
      "evaluationId": 1236,
      "questionId": 2,
      "score": 8.5,
      "maxMarks": 10,
      "percentage": 85.0,
      "feedback": "Good explanation of differentiation. Example is correct.",
      "usedFallback": false
    },
    {
      "success": true,
      "evaluationId": 1237,
      "questionId": 3,
      "score": 10.0,
      "maxMarks": 10,
      "percentage": 100.0,
      "feedback": "Perfect answer. Formula is correct and complete.",
      "usedFallback": false
    }
  ]
}

EOF

echo "=== All Examples Complete! ==="
