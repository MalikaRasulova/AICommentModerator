using AICommentModerator.Application.Bot;
using Microsoft.AspNetCore.Mvc;

namespace AICommentModerator.Controllers;

[ApiController]
[Route("api/bot")]
public class BotConfigController : ControllerBase
{
    private readonly BotPolicy _policy;
    private readonly WorkingHoursCalendar _hours;
    private readonly DiscussionThreads _threads;

    public BotConfigController(BotPolicy policy, WorkingHoursCalendar hours, DiscussionThreads threads)
    {
        _policy = policy;
        _hours = hours;
        _threads = threads;
    }

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
                onlyUnderChannelPosts = bot.Replies.OnlyUnderChannelPosts,
                usePostAsContext = bot.Replies.UsePostAsContext,
                postsRemembered = _threads.Count,
                rules = bot.Replies.Rules.Select(r => new { r.Name, r.Match, patterns = r.Patterns, onlyFor = r.OnlyForUsernames })
            },
            workingHours = new
            {
                enabled = bot.WorkingHours.Enabled,
                timeZone = bot.WorkingHours.TimeZone,
                days = bot.WorkingHours.Days,
                from = bot.WorkingHours.From,
                to = bot.WorkingHours.To,
                holidays = bot.WorkingHours.Holidays,
                openNow = _hours.IsOpen(),
                outside = new
                {
                    moderate = bot.WorkingHours.Outside.Moderate,
                    notifyModerators = bot.WorkingHours.Outside.NotifyModerators,
                    replies = bot.WorkingHours.Outside.Replies
                }
            },
            perChatOverrides = bot.PerChat.Select(c => new { c.ChatId, c.Enabled, c.RepliesEnabled, c.RespondTo, extraBannedWords = c.ExtraBannedWords })
        });
    }
}
