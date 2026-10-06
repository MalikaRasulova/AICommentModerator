using AICommentModerator.Application.Bot;

namespace AICommentModerator.Application.Abstractions;

/// <summary>Decides whether a comment deserves an answer, and what that answer is.</summary>
public interface IReplyService
{
    Task<string?> TryGetReplyAsync(ReplyContext context, CancellationToken cancellationToken = default);
}
