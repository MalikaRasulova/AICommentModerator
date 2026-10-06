using AICommentModerator.Application.Bot;
using Microsoft.AspNetCore.Mvc;

namespace AICommentModerator.Controllers;

[ApiController]
[Route("api/bot")]
public class BotConfigController : ControllerBase
{
    private readonly BotPolicy _policy;

    public BotConfigController(BotPolicy policy) => _policy = policy;

    /// <summary>
    /// The configuration the bot is running with right now. bot.config.json is watched,
    /// so this reflects edits made since startup without a restart.
    /// </summary>
    [HttpGet("config")]
    public IActionResult Current()
    {
        var bot = _policy.Current;

        return Ok(new
        {
            username = bot.Username,
            scope = new
            {
                mode = bot.Scope.Mode,
                allowedChats = bot.Scope.AllowedChatIds,
                ignoreBots = bot.Scope.IgnoreBots,
                ignoreChannelPosts = bot.Scope.IgnoreChannelPosts,
                ignoreEditedMessages = bot.Scope.IgnoreEditedMessages
            },
            exempt = new
            {
                usernames = bot.Exempt.Usernames,
                userIds = bot.Exempt.UserIds,
                skipModeration = bot.Exempt.SkipModeration
            },
            actions = bot.Actions,
            moderatorsChat = bot.Moderators.IsConfigured ? bot.Moderators.ChatId.ToString() : "not set",
            replies = new
            {
                enabled = bot.Replies.Enabled,
                respondTo = bot.Replies.RespondTo,
                cooldownSeconds = bot.Replies.CooldownSeconds,
                maxPerChatPerHour = bot.Replies.MaxRepliesPerChatPerHour,
                aiWhenNoRuleMatches = bot.Replies.UseAiWhenNoRuleMatches,
                rules = bot.Replies.Rules.Select(r => new { r.Name, r.Match, patterns = r.Patterns, onlyFor = r.OnlyForUsernames })
            },
            perChatOverrides = bot.PerChat.Select(c => new { c.ChatId, c.Enabled, c.RepliesEnabled, c.RespondTo, extraBannedWords = c.ExtraBannedWords })
        });
    }
}
