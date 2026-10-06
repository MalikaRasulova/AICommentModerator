namespace AICommentModerator.Application.Models;

/// <summary>The verdict on a single comment.</summary>
/// <param name="Decision">What to do with the comment.</param>
/// <param name="Reason">Short, human readable explanation shown to moderators.</param>
/// <param name="Categories">Rule or category names that fired, e.g. "spam", "insult".</param>
/// <param name="Confidence">0..1. Rule hits report 1, the model reports its own number.</param>
/// <param name="Source">Who decided: "openai" or "rules".</param>
public sealed record ModerationResult(
    ModerationDecision Decision,
    string Reason,
    IReadOnlyList<string> Categories,
    double Confidence,
    string Source)
{
    public static ModerationResult Allowed(string source, string reason = "No rule matched") =>
        new(ModerationDecision.Allow, reason, Array.Empty<string>(), 1.0, source);
}
