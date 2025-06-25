namespace AICommentModerator.Domain
{
    public class Comment
    {
        public Guid Id { get; set; }
        public string Platform { get; set; }
        public string Text { get; set; }
        public DateTime ReceivedAt { get; set; }
    }
}
