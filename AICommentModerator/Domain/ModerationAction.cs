namespace AICommentModerator.Domain
{
    public class ModerationAction
    {
        public Guid Id { get; set; }
        public Guid CommentId { get; set; }
        public string Moderator { get; set; }
        public string ActionType { get; set; } // Approved, Rejected, Edited
        public string FinalText { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
