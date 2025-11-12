namespace SmartStudyFunc.Models
{
    /// <summary>
    /// Represents a chunk retrieved from the database with its similarity score
    /// </summary>
    public class SearchResultChunk
    {
        /// <summary>
        /// The unique identifier for this chunk
        /// </summary>
        public int ChunkId { get; set; }

        /// <summary>
        /// The text content of the chunk
        /// </summary>
        public string ChunkText { get; set; } = string.Empty;

        /// <summary>
        /// The embedding vector as byte array
        /// </summary>
        public byte[] Embedding { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Cosine similarity score between query and chunk (0-1)
        /// </summary>
        public double Similarity { get; set; }

        /// <summary>
        /// The file this chunk belongs to
        /// </summary>
        public int FileId { get; set; }

        /// <summary>
        /// The sequence number of this chunk within the file
        /// </summary>
        public int ChunkIndex { get; set; }
    }
}
