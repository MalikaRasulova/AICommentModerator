using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Models;
using AICommentModerator.Application.Options;
using AICommentModerator.Controllers;
using AICommentModerator.Domain.Telegram;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AICommentModerator.Tests;

public class TelegramWebhookControllerTests
{
    [Fact]
    public async Task Blocked_comment_is_deleted_and_logged()
    {
        var telegram = new FakeTelegramClient();
        var audit = new FakeAuditLog();
        var controller = Build(ModerationDecision.Block, telegram, audit);

        var result = await controller.Receive(Update("you are an idiot"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal((42L, 7L), telegram.Deleted.Single());
        Assert.True(audit.Entries.Single().Deleted);
    }

    [Fact]
    public async Task Allowed_comment_is_kept_and_still_logged()
    {
        var telegram = new FakeTelegramClient();
        var audit = new FakeAuditLog();
        var controller = Build(ModerationDecision.Allow, telegram, audit);

        await controller.Receive(Update("nice work"), CancellationToken.None);

        Assert.Empty(telegram.Deleted);
        Assert.Single(audit.Entries);
        Assert.False(audit.Entries.Single().Deleted);
    }

    [Fact]
    public async Task Updates_without_text_are_ignored()
    {
        var telegram = new FakeTelegramClient();
        var audit = new FakeAuditLog();
        var controller = Build(ModerationDecision.Block, telegram, audit);

        var update = new TelegramUpdate { Message = new TelegramMessage { MessageId = 7, Chat = new TelegramChat { Id = 42 } } };
        await controller.Receive(update, CancellationToken.None);

        Assert.Empty(telegram.Deleted);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Messages_from_bots_are_ignored()
    {
        var telegram = new FakeTelegramClient();
        var audit = new FakeAuditLog();
        var controller = Build(ModerationDecision.Block, telegram, audit);

        var update = Update("you are an idiot");
        update.Message!.From = new TelegramUser { Id = 1, IsBot = true };

        await controller.Receive(update, CancellationToken.None);

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Wrong_secret_header_is_rejected()
    {
        var telegram = new FakeTelegramClient();
        var audit = new FakeAuditLog();
        var controller = Build(ModerationDecision.Block, telegram, audit, secret: "expected-secret");
        controller.ControllerContext.HttpContext.Request.Headers[TelegramWebhookController.SecretHeader] = "wrong";

        var result = await controller.Receive(Update("you are an idiot"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Matching_secret_header_is_accepted()
    {
        var telegram = new FakeTelegramClient();
        var audit = new FakeAuditLog();
        var controller = Build(ModerationDecision.Allow, telegram, audit, secret: "expected-secret");
        controller.ControllerContext.HttpContext.Request.Headers[TelegramWebhookController.SecretHeader] = "expected-secret";

        var result = await controller.Receive(Update("hello"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(audit.Entries);
    }

    private static TelegramUpdate Update(string text) => new()
    {
        UpdateId = 1,
        Message = new TelegramMessage
        {
            MessageId = 7,
            Chat = new TelegramChat { Id = 42, Type = "supergroup" },
            From = new TelegramUser { Id = 5, Username = "someone" },
            Text = text
        }
    };

    private static TelegramWebhookController Build(
        ModerationDecision decision,
        FakeTelegramClient telegram,
        FakeAuditLog audit,
        string? secret = null)
    {
        var options = new TelegramOptions { BotToken = "test-token", WebhookSecret = secret, DeleteBlockedMessages = true };

        var controller = new TelegramWebhookController(
            new FakeModerationService(decision),
            telegram,
            audit,
            new StaticOptionsMonitor<TelegramOptions>(options),
            NullLogger<TelegramWebhookController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        return controller;
    }

    private sealed class FakeModerationService : IModerationService
    {
        private readonly ModerationDecision _decision;

        public FakeModerationService(ModerationDecision decision) => _decision = decision;

        public Task<ModerationResult> ModerateAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModerationResult(_decision, "test", new[] { "test" }, 1, "fake"));
    }

    private sealed class FakeTelegramClient : ITelegramClient
    {
        public List<(long ChatId, long MessageId)> Deleted { get; } = new();

        public List<string> Sent { get; } = new();

        public Task<bool> SendMessageAsync(long chatId, string text, long? replyToMessageId = null, CancellationToken cancellationToken = default)
        {
            Sent.Add(text);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteMessageAsync(long chatId, long messageId, CancellationToken cancellationToken = default)
        {
            Deleted.Add((chatId, messageId));
            return Task.FromResult(true);
        }

        public Task<string?> GetMeAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("test_bot");
    }

    private sealed class FakeAuditLog : IAuditLog
    {
        public List<(string Text, ModerationResult Result, bool Deleted)> Entries { get; } = new();

        public Task RecordAsync(string platform, long chatId, long messageId, string? author, string text, ModerationResult result, bool deleted, CancellationToken cancellationToken = default)
        {
            Entries.Add((text, result, deleted));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ModerationRecord>> RecentAsync(int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModerationRecord>>(Array.Empty<ModerationRecord>());
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable OnChange(Action<T, string?> listener) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
