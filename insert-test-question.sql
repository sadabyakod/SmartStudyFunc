-- Insert test question for answer sheet evaluation
DECLARE @QuestionId UNIQUEIDENTIFIER = NEWID();
DECLARE @ExamId VARCHAR(50) = 'EXAM_12345';

-- Insert into ExamQuestions table
INSERT INTO ExamQuestions (
    Id,
    ExamId,
    QuestionNumber,
    QuestionText,
    ModelAnswer,
    MaxScore,
    Keywords,
    CreatedAt
)
VALUES (
    @QuestionId,
    @ExamId,
    1,
    'Part-B (5 marks): Find dy/dx for the function y = (x^2 + 3x)(e^x)',
    'To find dy/dx, apply the product rule: d/dx(uv) = u(dv/dx) + v(du/dx). Let u = x^2 + 3x, then du/dx = 2x + 3. Let v = e^x, then dv/dx = e^x. Therefore, dy/dx = (x^2 + 3x)(e^x) + (e^x)(2x + 3) = e^x(x^2 + 3x + 2x + 3) = e^x(x^2 + 5x + 3).',
    20,
    'product rule,differentiation,calculus,exponential function,chain rule,derivative,simplification',
    GETUTCDATE()
);

-- Display the generated Question ID for use in testing
SELECT 
    @QuestionId AS QuestionId,
    @ExamId AS ExamId,
    'Question created successfully. Use this QuestionId for testing.' AS Message;
