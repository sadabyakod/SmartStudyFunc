using System.Threading;
using System.Threading.Tasks;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// Routes evaluation requests to appropriate subject-specific engines
    /// Central orchestrator for the evaluation pipeline
    /// </summary>
    public interface ISubjectRouter
    {
        /// <summary>
        /// Routes evaluation to the correct engine based on classification
        /// Falls back gracefully if specialized engine unavailable
        /// </summary>
        Task<EvaluationEngineResult> RouteAndEvaluateAsync(
            EvaluationContext context,
            CancellationToken cancellationToken = default);
    }
}
