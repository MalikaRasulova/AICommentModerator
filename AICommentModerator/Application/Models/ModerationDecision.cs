namespace AICommentModerator.Application.Models;

/// <summary>What should happen to a comment.</summary>
public enum ModerationDecision
{
    /// <summary>Nothing wrong with it - leave it alone.</summary>
    Allow,

    /// <summary>Questionable - keep it, but tell the moderators.</summary>
    Flag,

    /// <summary>Clearly against the rules - delete it.</summary>
    Block
}
