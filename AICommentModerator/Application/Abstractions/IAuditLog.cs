using AICommentModerator.Application.Models;

namespace AICommentModerator.Application.Abstractions;

/// <summary>Stores every comment and every decision taken on it.</summary>
public interface IAuditLog
{
    Task RecordAsync(
        string platform,
        long chatId,
        long messageId,
        string? author,
        string text,
        ModerationResult result,
        bool deleted,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModerationRecord>> RecentAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>One row of the moderation history, as the API returns it.</summary>
public sealed record ModerationRecord(
    Guid Id,
    string Platform,
    string? Author,
    string Text,
    string Decision,
    string Reason,
    string Source,
    bool Deleted,
    DateTime CreatedAt);
