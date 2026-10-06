using System.Collections.Concurrent;
using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Models;

namespace AICommentModerator.Infrastructure.Persistence;

/// <summary>
/// Used when no database is configured, so the service still runs and can be demoed.
/// Keeps the last few hundred decisions in memory and forgets them on restart.
/// </summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private const int Capacity = 500;

    private readonly ConcurrentQueue<ModerationRecord> _records = new();

    public Task RecordAsync(
        string platform,
        long chatId,
        long messageId,
        string? author,
        string text,
        ModerationResult result,
        bool deleted,
        CancellationToken cancellationToken = default)
    {
        _records.Enqueue(new ModerationRecord(
            Guid.NewGuid(),
            platform,
            author,
            text,
            result.Decision.ToString(),
            result.Reason,
            result.Source,
            deleted,
            DateTime.UtcNow));

        while (_records.Count > Capacity && _records.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ModerationRecord>> RecentAsync(int take, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModerationRecord> result = _records
            .Reverse()
            .Take(take)
            .ToList();

        return Task.FromResult(result);
    }
}
