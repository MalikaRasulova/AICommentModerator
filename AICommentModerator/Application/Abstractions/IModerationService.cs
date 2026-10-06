using AICommentModerator.Application.Models;

namespace AICommentModerator.Application.Abstractions;

/// <summary>Decides whether a comment may stay.</summary>
public interface IModerationService
{
    /// <param name="text">The comment.</param>
    /// <param name="extraBannedWords">Words banned only in the chat this comment came from.</param>
    Task<ModerationResult> ModerateAsync(
        string text,
        IReadOnlyCollection<string>? extraBannedWords = null,
        CancellationToken cancellationToken = default);
}
