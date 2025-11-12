using System.Collections.Generic;

namespace SmartStudyFunc.Models
{
    /// <summary>
    /// Response model for RAG search queries
    /// </summary>
    public class SearchResponse
    {
        /// <summary>
        /// The generated answer from GPT-4o-mini based on retrieved context
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// List of chunk IDs that were used to generate the answer
        /// </summary>
        public List<int> ChunksUsed { get; set; } = new List<int>();

        /// <summary>
        /// Confidence score (0-1) based on highest cosine similarity
        /// </summary>
        public double Confidence { get; set; }
    }
}
