using AICommentModerator.Application.Models;
using AICommentModerator.Application.Moderation;
using AICommentModerator.Application.Options;
using Xunit;

namespace AICommentModerator.Tests;

public class RuleBasedModerationServiceTests
{
    private static RuleBasedModerationService Create(Action<ModerationOptions>? configure = null)
    {
        var options = new ModerationOptions
        {
            BannedWords = new[] { "idiot", "ahmoq" },
            SuspiciousWords = new[] { "casino" },
            MaxLinks = 2
        };

        configure?.Invoke(options);
        return new RuleBasedModerationService(new TestOptionsMonitor<ModerationOptions>(options));
    }

    [Theory]
    [InlineData("Thanks, this helped a lot")]
    [InlineData("Men bu bilan rozi emasman, lekin tushundim")]
    [InlineData("Where can I read the documentation?")]
    public void Ordinary_comments_pass(string text)
    {
        var result = Create().Evaluate(text);
        Assert.Equal(ModerationDecision.Allow, result.Decision);
    }

    [Theory]
    [InlineData("you are an idiot")]
    [InlineData("Sen ahmoq ekansan!")]
    [InlineData("IDIOT")]
    public void Banned_words_are_blocked(string text)
    {
        var result = Create().Evaluate(text);

        Assert.Equal(ModerationDecision.Block, result.Decision);
        Assert.Contains(result.Categories, c => c.StartsWith("banned-word"));
    }

    [Fact]
    public void Part_of_a_longer_word_is_not_a_banned_word()
    {
        var result = Create().Evaluate("idiotic weather today, is it not");
        Assert.Equal(ModerationDecision.Allow, result.Decision);
    }

    [Fact]
    public void Too_many_links_are_spam()
    {
        var result = Create().Evaluate("buy here https://a.com https://b.com https://c.com");

        Assert.Equal(ModerationDecision.Block, result.Decision);
        Assert.Contains(result.Categories, c => c.StartsWith("link-spam"));
    }

    [Fact]
    public void One_link_with_a_channel_invite_is_flagged()
    {
        var result = Create().Evaluate("obuna bo'ling t.me/somechannel");

        Assert.Equal(ModerationDecision.Flag, result.Decision);
        Assert.Contains("channel-promotion", result.Categories);
    }

    [Fact]
    public void Suspicious_words_are_flagged_not_blocked()
    {
        var result = Create().Evaluate("try this casino tonight");

        Assert.Equal(ModerationDecision.Flag, result.Decision);
        Assert.Contains(result.Categories, c => c.StartsWith("suspicious-word"));
    }

    [Fact]
    public void Shouting_is_flagged()
    {
        var result = Create().Evaluate("THIS IS COMPLETELY UNACCEPTABLE");

        Assert.Equal(ModerationDecision.Flag, result.Decision);
        Assert.Contains("shouting", result.Categories);
    }

    [Fact]
    public void Short_uppercase_words_are_not_shouting()
    {
        var result = Create().Evaluate("OK FINE");
        Assert.Equal(ModerationDecision.Allow, result.Decision);
    }

    [Fact]
    public void Long_character_runs_are_flagged()
    {
        var result = Create().Evaluate("whaaaaaaaaat is going on");

        Assert.Equal(ModerationDecision.Flag, result.Decision);
        Assert.Contains("repeated-characters", result.Categories);
    }

    [Fact]
    public void Overlong_comments_are_blocked()
    {
        var result = Create(o => o.MaxLength = 20).Evaluate(new string('a', 25) + " text");

        Assert.Equal(ModerationDecision.Block, result.Decision);
        Assert.Contains("too-long", result.Categories);
    }

    [Fact]
    public void A_chat_can_add_its_own_banned_words()
    {
        var service = Create();

        Assert.Equal(ModerationDecision.Allow, service.Evaluate("no spoilers here please").Decision);

        var result = service.Evaluate("no spoilers here please", new[] { "spoilers" });
        Assert.Equal(ModerationDecision.Block, result.Decision);
        Assert.Contains(result.Categories, c => c.StartsWith("banned-word"));
    }

    [Fact]
    public void Empty_text_is_allowed()
    {
        var result = Create().Evaluate("   ");
        Assert.Equal(ModerationDecision.Allow, result.Decision);
    }

}
