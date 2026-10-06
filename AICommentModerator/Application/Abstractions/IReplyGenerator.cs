namespace AICommentModerator.Application.Abstractions;

/// <summary>Writes a free-form answer when no canned rule fits.</summary>
public interface IReplyGenerator
{
    Task<string?> GenerateAsync(string comment, CancellationToken cancellationToken = default);
}
