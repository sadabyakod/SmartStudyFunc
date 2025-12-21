using System.Threading;
using System.Threading.Tasks;
using SmartStudyFunc.Models;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// Classifies questions into subject and type categories
    /// Used by router to select appropriate evaluation engine
    /// </summary>
    public interface IQuestionClassifier
    {
        /// <summary>
        /// Classifies a question based on text patterns, keywords, and context
        /// </summary>
        Task<QuestionClassification> ClassifyAsync(
            string questionText,
            string? syllabusContext = null,
            CancellationToken cancellationToken = default);
    }
}
