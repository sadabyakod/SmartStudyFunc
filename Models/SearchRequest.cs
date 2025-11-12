using System.ComponentModel.DataAnnotations;

namespace SmartStudyFunc.Models
{
    /// <summary>
    /// Request model for RAG search queries
    /// </summary>
    public class SearchRequest
    {
        /// <summary>
        /// The user's question to search for
        /// </summary>
        [Required(ErrorMessage = "Question is required")]
        public string Question { get; set; } = string.Empty;
    }
}
