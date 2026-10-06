using AICommentModerator.Application.Bot;
using AICommentModerator.Application.Models;
using AICommentModerator.Application.Options;
using Xunit;

namespace AICommentModerator.Tests;

public class BotPolicyTests
{
    [Fact]
    public void All_chats_mode_handles_any_chat()
    {
        var policy = Policy(new BotOptions());
        Assert.True(policy.HandlesChat(-100123));
    }

    [Fact]
    public void Allowlist_mode_handles_only_listed_chats()
    {
        var policy = Policy(new BotOptions
        {
            Scope = new ScopeOptions { Mode = "Allowlist", AllowedChatIds = new[] { -100123L } }
        });

        Assert.True(policy.HandlesChat(-100123));
        Assert.False(policy.HandlesChat(-100999));
    }

    [Fact]
    public void A_chat_can_be_switched_off_by_override()
    {
        var policy = Policy(new BotOptions
        {
            PerChat = { new ChatOverride { ChatId = -100123, Enabled = false } }
        });

        Assert.False(policy.HandlesChat(-100123));
        Assert.True(policy.HandlesChat(-100999));
    }

    [Theory]
    [InlineData("malika53", true)]
    [InlineData("@malika53", true)]
    [InlineData("MALIKA53", true)]
    [InlineData("someone", false)]
    public void Exempt_usernames_are_recognised_whatever_the_casing(string username, bool expected)
    {
        var policy = Policy(new BotOptions
        {
            Exempt = new ExemptOptions { Usernames = new[] { "malika53" } }
        });

        Assert.Equal(expected, policy.IsExempt(1, username));
    }

    [Fact]
    public void Exempt_user_ids_work_without_a_username()
    {
        var policy = Policy(new BotOptions { Exempt = new ExemptOptions { UserIds = new[] { 777L } } });

        Assert.True(policy.IsExempt(777, null));
        Assert.False(policy.IsExempt(778, null));
    }

    [Fact]
    public void Actions_are_taken_from_the_matching_decision()
    {
        var policy = Policy(new BotOptions());

        Assert.True(policy.ActionsFor(ModerationDecision.Block).Delete);
        Assert.False(policy.ActionsFor(ModerationDecision.Flag).Delete);
        Assert.True(policy.ActionsFor(ModerationDecision.Flag).NotifyModerators);
        Assert.False(policy.ActionsFor(ModerationDecision.Allow).NotifyModerators);
    }

    [Fact]
    public void A_chat_override_can_turn_replies_off_without_touching_the_rules()
    {
        var options = new BotOptions
        {
            Replies = new ReplyOptions { Enabled = true, RespondTo = "Everyone", Rules = { new ReplyRule { Name = "r" } } },
            PerChat = { new ChatOverride { ChatId = -100123, RepliesEnabled = false } }
        };

        var policy = Policy(options);

        Assert.False(policy.RepliesFor(-100123).Enabled);
        Assert.True(policy.RepliesFor(-100999).Enabled);
        Assert.Single(policy.RepliesFor(-100123).Rules);
    }

    [Fact]
    public void Extra_banned_words_are_per_chat()
    {
        var policy = Policy(new BotOptions
        {
            PerChat = { new ChatOverride { ChatId = -100123, ExtraBannedWords = new[] { "spoiler" } } }
        });

        Assert.Equal(new[] { "spoiler" }, policy.ExtraBannedWords(-100123));
        Assert.Empty(policy.ExtraBannedWords(-100999));
    }

    private static BotPolicy Policy(BotOptions options) => new(new TestOptionsMonitor<BotOptions>(options));
}
