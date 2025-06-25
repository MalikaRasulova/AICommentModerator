namespace AICommentModerator.Domain
{
    public class AIResponse
    {
        public Guid Id { get; set; }
        public Guid CommentId { get; set; }
        public string GeneratedText { get; set; }
        public DateTime GeneratedAt { get; set; }
    }
}
