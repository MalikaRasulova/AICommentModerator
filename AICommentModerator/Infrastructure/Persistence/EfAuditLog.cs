using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Models;
using AICommentModerator.Domain;
using Microsoft.EntityFrameworkCore;

namespace AICommentModerator.Infrastructure.Persistence;

/// <summary>Writes the history to PostgreSQL.</summary>
public class EfAuditLog : IAuditLog
{
    private readonly ApplicationDbContext _db;

    public EfAuditLog(ApplicationDbContext db) => _db = db;

    public virtual async Task RecordAsync(
        string platform,
        long chatId,
        long messageId,
        string? author,
        string text,
        ModerationResult result,
        bool deleted,
        CancellationToken cancellationToken = default)
    {
        var comment = new Comment
        {
            Platform = platform,
            ChatId = chatId,
            MessageId = messageId,
            Author = author,
            Text = Truncate(text, 4096),
            ReceivedAt = DateTime.UtcNow
        };

        comment.Action = new ModerationAction
        {
            CommentId = comment.Id,
            Decision = result.Decision.ToString(),
            Reason = Truncate(result.Reason, 512),
            Categories = Truncate(string.Join(",", result.Categories), 512),
            Confidence = result.Confidence,
            Source = result.Source,
            Deleted = deleted,
            DecidedAt = DateTime.UtcNow
        };

        _db.Comments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public virtual async Task<IReadOnlyList<ModerationRecord>> RecentAsync(int take, CancellationToken cancellationToken = default)
    {
        var rows = await _db.Comments
            .AsNoTracking()
            .Include(c => c.Action)
            .OrderByDescending(c => c.ReceivedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(c => new ModerationRecord(
            c.Id,
            c.Platform,
            c.Author,
            c.Text,
            c.Action?.Decision ?? "Unknown",
            c.Action?.Reason ?? string.Empty,
            c.Action?.Source ?? string.Empty,
            c.Action?.Deleted ?? false,
            c.ReceivedAt)).ToList();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
