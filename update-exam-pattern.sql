-- Update Karnataka 2nd PUC Maths Exam Pattern to match official structure
-- Theory: 80 marks (Part A: 20, Part B: 12, Part C: 18, Part D: 20, Part E: 10)
-- Internal Assessment: 20 marks
-- Total: 100 marks

-- First, check current exam structure
SELECT 
    ExamId,
    ExamName,
    CreatedAt,
    -- Extract part information
    JSON_VALUE(ExamContentJson, '$.parts[0].partName') as PartA_Name,
    JSON_VALUE(ExamContentJson, '$.parts[0].marksPerQuestion') as PartA_MarksPerQ,
    (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[0].questions')) as PartA_Questions,
    
    JSON_VALUE(ExamContentJson, '$.parts[1].partName') as PartB_Name,
    JSON_VALUE(ExamContentJson, '$.parts[1].marksPerQuestion') as PartB_MarksPerQ,
    (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[1].questions')) as PartB_Questions,
    
    JSON_VALUE(ExamContentJson, '$.parts[2].partName') as PartC_Name,
    JSON_VALUE(ExamContentJson, '$.parts[2].marksPerQuestion') as PartC_MarksPerQ,
    (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[2].questions')) as PartC_Questions,
    
    JSON_VALUE(ExamContentJson, '$.parts[3].partName') as PartD_Name,
    JSON_VALUE(ExamContentJson, '$.parts[3].marksPerQuestion') as PartD_MarksPerQ,
    (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[3].questions')) as PartD_Questions,
    
    JSON_VALUE(ExamContentJson, '$.parts[4].partName') as PartE_Name,
    JSON_VALUE(ExamContentJson, '$.parts[4].marksPerQuestion') as PartE_MarksPerQ,
    (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[4].questions')) as PartE_Questions
FROM GeneratedExams
WHERE ExamId LIKE '%Karnataka_2nd_PUC_Math%';

-- Calculate current totals
SELECT 
    ExamId,
    'Current Structure' as Type,
    CAST(JSON_VALUE(ExamContentJson, '$.parts[0].marksPerQuestion') AS INT) * 
        (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[0].questions')) as PartA_Total,
    CAST(JSON_VALUE(ExamContentJson, '$.parts[1].marksPerQuestion') AS INT) * 
        (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[1].questions')) as PartB_Total,
    CAST(JSON_VALUE(ExamContentJson, '$.parts[2].marksPerQuestion') AS INT) * 
        (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[2].questions')) as PartC_Total,
    CAST(JSON_VALUE(ExamContentJson, '$.parts[3].marksPerQuestion') AS INT) * 
        (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[3].questions')) as PartD_Total,
    CAST(JSON_VALUE(ExamContentJson, '$.parts[4].marksPerQuestion') AS INT) * 
        (SELECT COUNT(*) FROM OPENJSON(ExamContentJson, '$.parts[4].questions')) as PartE_Total
FROM GeneratedExams
WHERE ExamId LIKE '%Karnataka_2nd_PUC_Math%';

/*
CORRECT PATTERN (Official Karnataka 2nd PUC Maths):
==================================================
Part A: 20 questions × 1 mark = 20 marks (15 MCQs + 5 Fill-in-the-Blanks)
Part B: 6 out of 11 questions × 2 marks = 12 marks (Answer ANY 6)
Part C: 6 out of 11 questions × 3 marks = 18 marks (Answer ANY 6)
Part D: 4 out of 8 questions × 5 marks = 20 marks (Answer ANY 4)
Part E: 2 questions with choices × 5 marks each = 10 marks (6+4 choice format)
--------------------------------------------------
Theory Total: 80 marks
Internal Assessment: 20 marks
Grand Total: 100 marks
Passing: 35% (35/100)
==================================================

TO FIX THE EXAM:
1. Update Part A to have 20 questions (add 5 more MCQs or Fill-in-the-Blanks)
2. Keep Part A at 1 mark each = 20 marks
3. Adjust Part B-E to match the correct structure
4. Final totals: MCQ=20, Subjective=60, Theory Total=80

You need to manually edit the ExamContentJson to:
- Add 5 more questions to Part A (make it 20 total)
- Adjust other parts if needed to match official pattern
*/

-- To update, you would export the ExamContentJson, edit it, and run:
/*
UPDATE GeneratedExams
SET ExamContentJson = '<your corrected JSON here>',
    UpdatedAt = GETUTCDATE()
WHERE ExamId = 'Karnataka_2nd_PUC_Math_Model_Paper_2024-25_cached_141646';
*/

PRINT 'Please manually update the ExamContentJson to match the correct exam pattern shown above.';
