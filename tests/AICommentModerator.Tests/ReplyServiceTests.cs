using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Bot;
using AICommentModerator.Application.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AICommentModerator.Tests;

public class ReplyServiceTests
{
    [Fact]
    public async Task Keyword_rule_answers_the_comment()
    {
        var service = Service(Options());

        var reply = await service.TryGetReplyAsync(Context("Salom, savolim bor"));

        Assert.Equal("Salom!", reply);
    }

    [Fact]
    public async Task A_keyword_inside_a_longer_word_does_not_fire()
    {
        var service = Service(Options());

        Assert.Null(await service.TryGetReplyAsync(Context("salomatlik haqida savol")));
    }

    [Fact]
    public async Task Regex_rules_work_too()
    {
        var service = Service(Options());

        Assert.Equal("Narx: ...", await service.TryGetReplyAsync(Context("Bu qancha turadi?")));
    }

    [Fact]
    public async Task Replies_can_be_turned_off_completely()
    {
        var options = Options();
        options.Replies.Enabled = false;

        Assert.Null(await Service(options).TryGetReplyAsync(Context("Salom")));
    }

    [Fact]
    public async Task With_MentionsAndReplies_a_plain_comment_gets_nothing()
    {
        var options = Options();
        options.Replies.RespondTo = "MentionsAndReplies";

        var service = Service(options);

        Assert.Null(await service.TryGetReplyAsync(Context("Salom")));
        Assert.Equal("Salom!", await service.TryGetReplyAsync(Context("Salom") with { MentionsBot = true }));
    }

    [Fact]
    public async Task A_reply_to_the_bot_counts_as_an_invitation()
    {
        var options = Options();
        options.Replies.RespondTo = "MentionsAndReplies";

        var reply = await Service(options).TryGetReplyAsync(Context("Salom") with { IsReplyToBot = true });

        Assert.Equal("Salom!", reply);
    }

    [Fact]
    public async Task The_same_person_is_not_answered_twice_within_the_cooldown()
    {
        var options = Options();
        options.Replies.CooldownSeconds = 60;

        var now = DateTimeOffset.UtcNow;
        var service = Service(options, () => now);

        Assert.Equal("Salom!", await service.TryGetReplyAsync(Context("Salom")));
        Assert.Null(await service.TryGetReplyAsync(Context("Salom")));

        now = now.AddSeconds(61);
        Assert.Equal("Salom!", await service.TryGetReplyAsync(Context("Salom")));
    }

    [Fact]
    public async Task Another_person_in_the_same_chat_is_still_answered()
    {
        var options = Options();
        options.Replies.CooldownSeconds = 60;

        var service = Service(options);

        Assert.Equal("Salom!", await service.TryGetReplyAsync(Context("Salom")));
        Assert.Equal("Salom!", await service.TryGetReplyAsync(Context("Salom") with { UserId = 999 }));
    }

    [Fact]
    public async Task The_hourly_ceiling_stops_a_flood()
    {
        var options = Options();
        options.Replies.CooldownSeconds = 0;
        options.Replies.MaxRepliesPerChatPerHour = 2;

        var service = Service(options);

        Assert.NotNull(await service.TryGetReplyAsync(Context("Salom") with { UserId = 1 }));
        Assert.NotNull(await service.TryGetReplyAsync(Context("Salom") with { UserId = 2 }));
        Assert.Null(await service.TryGetReplyAsync(Context("Salom") with { UserId = 3 }));
    }

    [Fact]
    public async Task A_rule_can_be_limited_to_certain_people()
    {
        var options = Options();
        options.Replies.Rules.Insert(0, new ReplyRule
        {
            Name = "vip",
            Match = "Keyword",
            Patterns = new[] { "salom" },
            Reply = "Xush kelibsiz!",
            OnlyForUsernames = new[] { "boss" }
        });

        var service = Service(options);

        Assert.Equal("Xush kelibsiz!", await service.TryGetReplyAsync(Context("Salom") with { Username = "boss" }));
        Assert.Equal("Salom!", await service.TryGetReplyAsync(Context("Salom") with { UserId = 2, Username = "someone" }));
    }

    [Fact]
    public async Task Without_a_matching_rule_the_bot_stays_quiet_unless_ai_is_allowed()
    {
        var options = Options();

        Assert.Null(await Service(options).TryGetReplyAsync(Context("Bugun ob-havo qanday?")));

        options.Replies.UseAiWhenNoRuleMatches = true;
        var withAi = Service(options, generator: new FixedGenerator("Model javobi"));

        Assert.Equal("Model javobi", await withAi.TryGetReplyAsync(Context("Bugun ob-havo qanday?")));
    }

    [Fact]
    public async Task A_failing_generator_does_not_break_the_webhook()
    {
        var options = Options();
        options.Replies.UseAiWhenNoRuleMatches = true;

        var service = Service(options, generator: new ThrowingGenerator());

        Assert.Null(await service.TryGetReplyAsync(Context("Bugun ob-havo qanday?")));
    }

    [Fact]
    public async Task A_broken_regex_in_the_config_is_skipped()
    {
        var options = Options();
        options.Replies.Rules.Insert(0, new ReplyRule
        {
            Name = "broken",
            Match = "Regex",
            Patterns = new[] { "([unclosed" },
            Reply = "never"
        });

        Assert.Equal("Salom!", await Service(options).TryGetReplyAsync(Context("Salom")));
    }

    private static BotOptions Options() => new()
    {
        Replies = new ReplyOptions
        {
            Enabled = true,
            RespondTo = "Everyone",
            CooldownSeconds = 0,
            MaxRepliesPerChatPerHour = 100,
            Rules =
            {
                new ReplyRule { Name = "greeting", Match = "Keyword", Patterns = new[] { "salom" }, Reply = "Salom!" },
                new ReplyRule { Name = "price", Match = "Regex", Patterns = new[] { "narx|qancha turadi" }, Reply = "Narx: ..." }
            }
        }
    };

    private static ReplyService Service(BotOptions options, Func<DateTimeOffset>? clock = null, IReplyGenerator? generator = null) =>
        new(new BotPolicy(new TestOptionsMonitor<BotOptions>(options)), NullLogger<ReplyService>.Instance, generator)
        {
            Now = clock ?? (() => DateTimeOffset.UtcNow)
        };

    private static ReplyContext Context(string text) => new(-100123, 1, "someone", text, false, false);

    private sealed class FixedGenerator : IReplyGenerator
    {
        private readonly string _reply;

        public FixedGenerator(string reply) => _reply = reply;

        public Task<string?> GenerateAsync(string comment, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(_reply);
    }

    private sealed class ThrowingGenerator : IReplyGenerator
    {
        public Task<string?> GenerateAsync(string comment, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("model unreachable");
    }
}
