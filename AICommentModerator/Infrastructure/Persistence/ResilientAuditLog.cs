using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Models;

namespace AICommentModerator.Infrastructure.Persistence;

/// <summary>
/// Writes to the database, and keeps working when the database does not.
/// A dead PostgreSQL must never stop the bot from deleting an offending comment, so a
/// failed write is logged and kept in memory instead of bubbling up as a 500 to Telegram.
/// </summary>
public sealed class ResilientAuditLog : IAuditLog
{
    private readonly EfAuditLog _database;
    private readonly InMemoryAuditLog _memory;
    private readonly ILogger<ResilientAuditLog> _logger;

    public ResilientAuditLog(EfAuditLog database, InMemoryAuditLog memory, ILogger<ResilientAuditLog> logger)
    {
        _database = database;
        _memory = memory;
        _logger = logger;
    }

    public async Task RecordAsync(
        string platform,
        long chatId,
        long messageId,
        string? author,
        string text,
        ModerationResult result,
        bool deleted,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _database.RecordAsync(platform, chatId, messageId, author, text, result, deleted, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write the audit record to the database; keeping it in memory");
            await _memory.RecordAsync(platform, chatId, messageId, author, text, result, deleted, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ModerationRecord>> RecentAsync(int take, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _database.RecentAsync(take, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the audit log from the database; serving what is in memory");
            return await _memory.RecentAsync(take, cancellationToken);
        }
    }
}
