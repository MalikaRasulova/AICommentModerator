using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Bot;
using AICommentModerator.Application.Models;
using AICommentModerator.Application.Options;
using AICommentModerator.Controllers;
using AICommentModerator.Domain.Telegram;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AICommentModerator.Tests;

public class TelegramWebhookControllerTests
{
    [Fact]
    public async Task Blocked_comment_is_deleted_and_logged()
    {
        var harness = new Harness(ModerationDecision.Block);

        var result = await harness.Receive(Update("you are an idiot"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal((42L, 7L), harness.Telegram.Deleted.Single());
        Assert.True(harness.Audit.Entries.Single().Deleted);
    }

    [Fact]
    public async Task Allowed_comment_is_kept_and_still_logged()
    {
        var harness = new Harness(ModerationDecision.Allow);

        await harness.Receive(Update("nice work"));

        Assert.Empty(harness.Telegram.Deleted);
        Assert.False(harness.Audit.Entries.Single().Deleted);
    }

    [Fact]
    public async Task Updates_without_text_are_ignored()
    {
        var harness = new Harness(ModerationDecision.Block);

        await harness.Receive(new TelegramUpdate
        {
            Message = new TelegramMessage { MessageId = 7, Chat = new TelegramChat { Id = 42 } }
        });

        Assert.Empty(harness.Telegram.Deleted);
        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task Messages_from_bots_are_ignored()
    {
        var harness = new Harness(ModerationDecision.Block);

        var update = Update("you are an idiot");
        update.Message!.From = new TelegramUser { Id = 1, IsBot = true };

        await harness.Receive(update);

        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task Channel_posts_are_ignored_when_the_config_says_so()
    {
        var harness = new Harness(ModerationDecision.Block);
        harness.Bot.Scope.IgnoreChannelPosts = true;

        await harness.Receive(new TelegramUpdate
        {
            ChannelPost = new TelegramMessage
            {
                MessageId = 9,
                Chat = new TelegramChat { Id = 42, Type = "channel" },
                Text = "you are an idiot"
            }
        });

        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task A_chat_outside_the_allowlist_is_left_alone()
    {
        var harness = new Harness(ModerationDecision.Block);
        harness.Bot.Scope.Mode = "Allowlist";
        harness.Bot.Scope.AllowedChatIds = new[] { -999L };

        await harness.Receive(Update("you are an idiot"));

        Assert.Empty(harness.Telegram.Deleted);
        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task An_exempt_author_is_never_deleted()
    {
        var harness = new Harness(ModerationDecision.Block);
        harness.Bot.Exempt.Usernames = new[] { "someone" };

        await harness.Receive(Update("you are an idiot"));

        Assert.Empty(harness.Telegram.Deleted);
        var entry = harness.Audit.Entries.Single();
        Assert.False(entry.Deleted);
        Assert.Equal("exempt", entry.Result.Source);
    }

    [Fact]
    public async Task Moderators_are_notified_about_a_blocked_comment()
    {
        var harness = new Harness(ModerationDecision.Block);
        harness.Bot.Moderators.ChatId = -500;

        await harness.Receive(Update("you are an idiot"));

        Assert.Contains(harness.Telegram.Sent, m => m.ChatId == -500 && m.Text.Contains("Block"));
    }

    [Fact]
    public async Task The_author_is_warned_in_the_chat_after_a_removal()
    {
        var harness = new Harness(ModerationDecision.Block);

        await harness.Receive(Update("you are an idiot"));

        Assert.Contains(harness.Telegram.Sent, m => m.ChatId == 42 && m.Text.StartsWith("A comment was removed"));
    }

    [Fact]
    public async Task An_allowed_comment_can_get_an_answer()
    {
        var harness = new Harness(ModerationDecision.Allow, reply: "Salom!");

        var result = await harness.Receive(Update("salom"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Contains(harness.Telegram.Sent, m => m.ChatId == 42 && m.Text == "Salom!" && m.ReplyTo == 7);
    }

    [Fact]
    public async Task A_blocked_comment_never_gets_an_answer()
    {
        var harness = new Harness(ModerationDecision.Block, reply: "Salom!");

        await harness.Receive(Update("salom"));

        Assert.DoesNotContain(harness.Telegram.Sent, m => m.Text == "Salom!");
    }

    [Fact]
    public async Task Wrong_secret_header_is_rejected()
    {
        var harness = new Harness(ModerationDecision.Block, secret: "expected-secret");
        harness.Controller.ControllerContext.HttpContext.Request.Headers[TelegramWebhookController.SecretHeader] = "wrong";

        var result = await harness.Receive(Update("you are an idiot"));

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task Matching_secret_header_is_accepted()
    {
        var harness = new Harness(ModerationDecision.Allow, secret: "expected-secret");
        harness.Controller.ControllerContext.HttpContext.Request.Headers[TelegramWebhookController.SecretHeader] = "expected-secret";

        var result = await harness.Receive(Update("hello"));

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(harness.Audit.Entries);
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

    private sealed class Harness
    {
        public Harness(ModerationDecision decision, string? reply = null, string? secret = null)
        {
            Bot = new BotOptions();
            Telegram = new FakeTelegramClient();
            Audit = new FakeAuditLog();

            var telegramOptions = new TelegramOptions { BotToken = "test-token", WebhookSecret = secret, DeleteBlockedMessages = true };

            Controller = new TelegramWebhookController(
                new FakeModerationService(decision),
                new FakeReplyService(reply),
                Telegram,
                Audit,
                new BotPolicy(new TestOptionsMonitor<BotOptions>(Bot)),
                new TestOptionsMonitor<TelegramOptions>(telegramOptions),
                NullLogger<TelegramWebhookController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        public BotOptions Bot { get; }

        public FakeTelegramClient Telegram { get; }

        public FakeAuditLog Audit { get; }

        public TelegramWebhookController Controller { get; }

        public Task<IActionResult> Receive(TelegramUpdate update) => Controller.Receive(update, CancellationToken.None);
    }

    private sealed class FakeModerationService : IModerationService
    {
        private readonly ModerationDecision _decision;

        public FakeModerationService(ModerationDecision decision) => _decision = decision;

        public Task<ModerationResult> ModerateAsync(string text, IReadOnlyCollection<string>? extraBannedWords = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModerationResult(_decision, "test", new[] { "test" }, 1, "fake"));
    }

    private sealed class FakeReplyService : IReplyService
    {
        private readonly string? _reply;

        public FakeReplyService(string? reply) => _reply = reply;

        public Task<string?> TryGetReplyAsync(ReplyContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(_reply);
    }

    private sealed class FakeTelegramClient : ITelegramClient
    {
        public List<(long ChatId, long MessageId)> Deleted { get; } = new();

        public List<(long ChatId, string Text, long? ReplyTo)> Sent { get; } = new();

        public Task<bool> SendMessageAsync(long chatId, string text, long? replyToMessageId = null, CancellationToken cancellationToken = default)
        {
            Sent.Add((chatId, text, replyToMessageId));
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
}
