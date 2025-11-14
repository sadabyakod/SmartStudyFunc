namespace SmartStudyFunc.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public Guid ConversationId { get; set; }
        public string Role { get; set; } = string.Empty; // "user" or "assistant"
        public string Message { get; set; } = string.Empty;
        public string? ChunksUsed { get; set; }
        public double? Confidence { get; set; }
        public DateTime CreatedOn { get; set; }
    }

    public class SearchRequestWithHistory : SearchRequest
    {
        public Guid? ConversationId { get; set; }
    }
}
