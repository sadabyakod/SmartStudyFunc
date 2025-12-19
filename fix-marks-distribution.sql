-- Fix subjectiveMaxScore from 90 to 85
-- The issue is that the marks in ExamContentJson add up to 90 instead of 85 for subjective questions

-- First, let's check the current exam definition
SELECT 
    ExamId,
    ExamName,
    LEN(ExamContentJson) as JsonLength,
    JSON_VALUE(ExamContentJson, '$.parts[0].marksPerQuestion') as PartA_MarksPerQuestion,
    JSON_VALUE(ExamContentJson, '$.parts[1].marksPerQuestion') as PartB_MarksPerQuestion,
    JSON_VALUE(ExamContentJson, '$.parts[2].marksPerQuestion') as PartC_MarksPerQuestion,
    JSON_VALUE(ExamContentJson, '$.parts[3].marksPerQuestion') as PartD_MarksPerQuestion,
    JSON_VALUE(ExamContentJson, '$.parts[4].marksPerQuestion') as PartE_MarksPerQuestion
FROM GeneratedExams
WHERE ExamId = 'Karnataka_2nd_PUC_Math_Model_Paper_2024-25_cached_141646';

-- Calculate total marks for each part
-- Part A (MCQ): 15 questions × 1 mark = 15 marks
-- Parts B-E (Subjective): Should total 85 marks

-- You need to manually edit the ExamContentJson to adjust the marks
-- The total for subjective questions should be 85, not 90

/*
Example breakdown that totals 100:
- Part A (MCQ): 15 × 1 = 15 marks
- Part B: 8 × 2 = 16 marks
- Part C: 8 × 3 = 24 marks  
- Part D: 6 × 5 = 30 marks
- Part E: 2 × 10 = 20 marks (changed from 2.5 × 10 = 25)
Total: 15 + 16 + 24 + 30 + 15 = 100 marks (85 subjective)

OR adjust Part E to 1.5 questions × 10 marks = 15 marks
OR reduce some marks from other parts
*/

-- To update, you would need to:
-- 1. Export the ExamContentJson
-- 2. Edit the JSON to adjust marks
-- 3. Update the record

-- Example query structure (DO NOT RUN without editing JSON):
/*
UPDATE GeneratedExams
SET ExamContentJson = '<your edited JSON here>'
WHERE ExamId = 'Karnataka_2nd_PUC_Math_Model_Paper_2024-25_cached_141646';
*/
