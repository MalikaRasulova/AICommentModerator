using AICommentModerator.Application.Models;

namespace AICommentModerator.Application.Abstractions;

/// <summary>Decides whether a comment may stay.</summary>
public interface IModerationService
{
    Task<ModerationResult> ModerateAsync(string text, CancellationToken cancellationToken = default);
}
