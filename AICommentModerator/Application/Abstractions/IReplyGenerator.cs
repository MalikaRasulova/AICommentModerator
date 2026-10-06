namespace AICommentModerator.Application.Abstractions;

/// <summary>Writes a free-form answer when no canned rule fits.</summary>
public interface IReplyGenerator
{
    /// <param name="comment">The comment to answer.</param>
    /// <param name="postText">The channel post it was left under, when known.</param>
    Task<string?> GenerateAsync(string comment, string? postText = null, CancellationToken cancellationToken = default);
}
