namespace AICommentModerator.Domain;

/// <summary>A comment exactly as it arrived from the platform.</summary>
public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>"telegram" today; the column leaves room for other sources.</summary>
    public string Platform { get; set; } = "telegram";

    public long ChatId { get; set; }

    public long MessageId { get; set; }

    /// <summary>Username or display name, when the platform sends one.</summary>
    public string? Author { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    public ModerationAction? Action { get; set; }
}
