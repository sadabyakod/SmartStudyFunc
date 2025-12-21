-- Create a fresh test question for complete end-to-end evaluation test
-- This will generate a new GUID for the question

DECLARE @NewQuestionId UNIQUEIDENTIFIER = NEWID();

INSERT INTO ExamQuestions (
    Id,
    ExamId,
    Text,
    Marks,
    ModelAnswer,
    Keywords,
    CreatedAt
)
VALUES (
    @NewQuestionId,
    12345,  -- Test exam ID
    'Part-B: Differentiate the following function with respect to x using product rule: y = (3x² + 2x)(5x - 1)',
    20,
    'Step 1: Identify u and v
Let u = 3x² + 2x and v = 5x - 1

Step 2: Find derivatives
du/dx = 6x + 2
dv/dx = 5

Step 3: Apply product rule
dy/dx = u(dv/dx) + v(du/dx)
dy/dx = (3x² + 2x)(5) + (5x - 1)(6x + 2)

Step 4: Expand first term
= 15x² + 10x + (5x - 1)(6x + 2)

Step 5: Expand second term
= 15x² + 10x + 30x² + 10x - 6x - 2

Step 6: Combine like terms
= 45x² + 14x - 2

Final Answer: dy/dx = 45x² + 14x - 2',
    'product rule, derivative, differentiate, du/dx, dv/dx, expand, combine like terms, 45x², 14x, -2',
    GETUTCDATE()
);

-- Return the generated Question ID
SELECT 
    @NewQuestionId AS QuestionId,
    'Question created successfully!' AS Status;
