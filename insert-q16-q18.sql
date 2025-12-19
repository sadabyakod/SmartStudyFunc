-- Insert test questions for real-test exam (Q16-Q18)
DELETE FROM ExamQuestions WHERE ExamId = 'real-test' AND QuestionNumber IN (16, 17, 18);

INSERT INTO ExamQuestions (ExamId, QuestionNumber, QuestionText, QuestionType, MaxScore, ModelAnswer, Rubric, Keywords, Subject, ChapterName)
VALUES 
('real-test', 16, 'What is 2+2?', 'Subjective', 2, '4', 'Award 2 marks for correct answer 4', 'addition,arithmetic,basic math', 'Mathematics', 'Basic Arithmetic'),
('real-test', 17, 'Introduce yourself', 'Subjective', 5, 'Hello, I am a student', 'Award marks for proper introduction with name', 'introduction,name,greeting', 'English', 'Communication'),
('real-test', 18, 'Name your country', 'Subjective', 3, 'India', 'Award full marks for correct country name', 'country,nation,geography', 'Social Studies', 'Geography');

SELECT * FROM ExamQuestions WHERE ExamId = 'real-test' AND QuestionNumber IN (16, 17, 18);
