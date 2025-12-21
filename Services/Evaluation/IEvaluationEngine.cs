using System.Threading;
using System.Threading.Tasks;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// Core interface for subject-specific evaluation engines
    /// All engines MUST implement this contract
    /// </summary>
    public interface IEvaluationEngine
    {
        /// <summary>
        /// Evaluates a student answer using subject-specific rules
        /// CRITICAL: OpenAI MUST NOT decide marks for Math/Science
        /// </summary>
        Task<EvaluationEngineResult> EvaluateAsync(
            EvaluationContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if this engine can handle the given subject/question type
        /// </summary>
        bool CanHandle(SubjectCategory subject, QuestionType questionType);

        /// <summary>
        /// Engine name for logging and auditing
        /// </summary>
        string EngineName { get; }
    }
}
