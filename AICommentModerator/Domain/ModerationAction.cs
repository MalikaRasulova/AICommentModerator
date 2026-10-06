namespace AICommentModerator.Domain;

/// <summary>What was decided about a comment, and who decided it.</summary>
public class ModerationAction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommentId { get; set; }

    public Comment? Comment { get; set; }

    /// <summary>Allow, Flag or Block.</summary>
    public string Decision { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>Rules or model categories that fired, comma separated.</summary>
    public string Categories { get; set; } = string.Empty;

    public double Confidence { get; set; }

    /// <summary>"openai" or "rules" - which engine produced the verdict.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>True when the message was actually removed from the chat.</summary>
    public bool Deleted { get; set; }

    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
}
