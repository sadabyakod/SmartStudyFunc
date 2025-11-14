using System.Collections.Generic;

namespace SmartStudyFunc.Models
{
    /// <summary>
    /// Request model for generating study notes
    /// </summary>
    public class StudyNotesRequest
    {
        /// <summary>
        /// Topic or subject for which to generate notes
        /// </summary>
        public string Topic { get; set; } = string.Empty;

        /// <summary>
        /// Format for the study notes
        /// Options: "bullet-points", "outline", "flashcards", "summary"
        /// Default: "bullet-points"
        /// </summary>
        public string? Format { get; set; }
    }

    /// <summary>
    /// Response model for generated study notes
    /// </summary>
    public class StudyNotesResponse
    {
        /// <summary>
        /// The topic that was requested
        /// </summary>
        public string Topic { get; set; } = string.Empty;

        /// <summary>
        /// The format used for notes generation
        /// </summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// The generated study notes
        /// </summary>
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// List of chunk IDs used to generate the notes
        /// </summary>
        public List<int> ChunksUsed { get; set; } = new List<int>();

        /// <summary>
        /// Number of chunks used
        /// </summary>
        public int ChunkCount { get; set; }
    }
}
