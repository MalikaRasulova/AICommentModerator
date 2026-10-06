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
    public async Task Outside_working_hours_the_bot_answers_with_the_away_message()
    {
        var options = Options();
        options.WorkingHours = Schedule();

        // Saturday, 22:00 in Tashkent - closed.
        var saturdayNight = new DateTimeOffset(2026, 10, 10, 17, 0, 0, TimeSpan.Zero);
        var reply = await Service(options, () => saturdayNight).TryGetReplyAsync(Context("Salom"));

        Assert.Equal("Ish vaqtimiz emas.", reply);
    }

    [Fact]
    public async Task Outside_working_hours_the_bot_can_stay_completely_silent()
    {
        var options = Options();
        options.WorkingHours = Schedule();
        options.WorkingHours.Outside.Replies = "Silent";

        var saturdayNight = new DateTimeOffset(2026, 10, 10, 17, 0, 0, TimeSpan.Zero);

        Assert.Null(await Service(options, () => saturdayNight).TryGetReplyAsync(Context("Salom")));
    }

    [Fact]
    public async Task During_working_hours_the_normal_rules_apply()
    {
        var options = Options();
        options.WorkingHours = Schedule();

        // Thursday 10:00 in Tashkent is 05:00 UTC.
        var thursdayMorning = new DateTimeOffset(2026, 10, 8, 5, 0, 0, TimeSpan.Zero);

        Assert.Equal("Salom!", await Service(options, () => thursdayMorning).TryGetReplyAsync(Context("Salom")));
    }

    private static WorkingHoursOptions Schedule() => new()
    {
        Enabled = true,
        TimeZone = "Asia/Tashkent",
        Days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
        From = "09:00",
        To = "18:00",
        Outside = new OutsideHoursOptions { Replies = "AutoReply", Message = "Ish vaqtimiz emas." }
    };

    [Fact]
    public async Task Ordinary_group_chatter_is_left_alone()
    {
        var options = Options();
        var plainChat = Context("Salom") with { IsUnderChannelPost = false, PostText = null };

        Assert.Null(await Service(options).TryGetReplyAsync(plainChat));

        options.Replies.OnlyUnderChannelPosts = false;
        Assert.Equal("Salom!", await Service(options).TryGetReplyAsync(plainChat));
    }

    [Fact]
    public async Task The_model_is_shown_the_post_the_comment_hangs_under()
    {
        var options = Options();
        options.Replies.UseAiWhenNoRuleMatches = true;

        var generator = new FixedGenerator("Javob");
        await Service(options, generator: generator).TryGetReplyAsync(Context("Bu qachon chiqadi?"));

        Assert.Equal("Yangi mahsulot haqida post", generator.SawPost);
    }

    [Fact]
    public async Task The_post_can_be_withheld_from_the_model()
    {
        var options = Options();
        options.Replies.UseAiWhenNoRuleMatches = true;
        options.Replies.UsePostAsContext = false;

        var generator = new FixedGenerator("Javob");
        await Service(options, generator: generator).TryGetReplyAsync(Context("Bu qachon chiqadi?"));

        Assert.Null(generator.SawPost);
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

    private static ReplyService Service(BotOptions options, Func<DateTimeOffset>? clock = null, IReplyGenerator? generator = null)
    {
        var monitor = new TestOptionsMonitor<BotOptions>(options);
        var now = clock ?? (() => DateTimeOffset.UtcNow);

        return new ReplyService(
            new BotPolicy(monitor),
            new WorkingHoursCalendar(monitor) { Now = now },
            NullLogger<ReplyService>.Instance,
            generator)
        {
            Now = now
        };
    }

    /// <summary>A comment under a channel post - the normal case for this bot.</summary>
    private static ReplyContext Context(string text) =>
        new(-100123, 1, "someone", text, false, false, IsUnderChannelPost: true, PostText: "Yangi mahsulot haqida post");

    private sealed class FixedGenerator : IReplyGenerator
    {
        private readonly string _reply;

        public FixedGenerator(string reply) => _reply = reply;

        public string? SawPost { get; private set; }

        public Task<string?> GenerateAsync(string comment, string? postText = null, CancellationToken cancellationToken = default)
        {
            SawPost = postText;
            return Task.FromResult<string?>(_reply);
        }
    }

    private sealed class ThrowingGenerator : IReplyGenerator
    {
        public Task<string?> GenerateAsync(string comment, string? postText = null, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("model unreachable");
    }
}
