using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Models;
using AICommentModerator.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AICommentModerator.Tests;

public class ResilientAuditLogTests
{
    [Fact]
    public async Task A_dead_database_does_not_lose_the_record()
    {
        var memory = new InMemoryAuditLog();
        var log = new ResilientAuditLog(new BrokenDatabaseLog(), memory, NullLogger<ResilientAuditLog>.Instance);

        await log.RecordAsync("telegram", 1, 2, "@someone", "spam text", Blocked(), deleted: true);

        var kept = await memory.RecentAsync(10);
        var record = Assert.Single(kept);
        Assert.Equal("spam text", record.Text);
        Assert.True(record.Deleted);
    }

    [Fact]
    public async Task Reading_falls_back_to_memory_too()
    {
        var memory = new InMemoryAuditLog();
        await memory.RecordAsync("telegram", 1, 2, "@someone", "earlier comment", Blocked(), deleted: false);

        var log = new ResilientAuditLog(new BrokenDatabaseLog(), memory, NullLogger<ResilientAuditLog>.Instance);

        var records = await log.RecentAsync(10);
        Assert.Single(records);
        Assert.Equal("earlier comment", records[0].Text);
    }

    private static ModerationResult Blocked() =>
        new(ModerationDecision.Block, "link spam", new[] { "link-spam:3" }, 1, "rules");

    /// <summary>Stands in for PostgreSQL being down.</summary>
    private sealed class BrokenDatabaseLog : EfAuditLog
    {
        public BrokenDatabaseLog() : base(null!) { }

        public override Task RecordAsync(string platform, long chatId, long messageId, string? author, string text, ModerationResult result, bool deleted, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is unreachable");

        public override Task<IReadOnlyList<ModerationRecord>> RecentAsync(int take, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is unreachable");
    }
}
