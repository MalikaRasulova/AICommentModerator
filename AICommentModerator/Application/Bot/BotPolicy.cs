using AICommentModerator.Application.Options;
using Microsoft.Extensions.Options;

namespace AICommentModerator.Application.Bot;

/// <summary>Reads bot.config.json and answers the questions the webhook asks of it.</summary>
public sealed class BotPolicy
{
    private readonly IOptionsMonitor<BotOptions> _options;

    public BotPolicy(IOptionsMonitor<BotOptions> options) => _options = options;

    public BotOptions Current => _options.CurrentValue;

    /// <summary>Is this chat one the bot works in at all?</summary>
    public bool HandlesChat(long chatId)
    {
        var options = Current;
        var chatOverride = FindOverride(chatId);

        if (chatOverride?.Enabled == false)
            return false;

        if (!options.Scope.IsAllowlist)
            return true;

        return options.Scope.AllowedChatIds.Contains(chatId);
    }

    /// <summary>Admins, owners, the team - whoever the config says to leave alone.</summary>
    public bool IsExempt(long userId, string? username)
    {
        var exempt = Current.Exempt;

        if (exempt.UserIds.Contains(userId))
            return true;

        if (string.IsNullOrWhiteSpace(username))
            return false;

        return exempt.Usernames.Any(u =>
            string.Equals(u.TrimStart('@'), username.TrimStart('@'), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What to do after a verdict, in this chat.</summary>
    public DecisionActions ActionsFor(Models.ModerationDecision decision) => decision switch
    {
        Models.ModerationDecision.Block => Current.Actions.OnBlock,
        Models.ModerationDecision.Flag => Current.Actions.OnFlag,
        _ => Current.Actions.OnAllow
    };

    /// <summary>Reply settings for this chat, with the per-chat override applied.</summary>
    public ReplyOptions RepliesFor(long chatId)
    {
        var global = Current.Replies;
        var chatOverride = FindOverride(chatId);

        if (chatOverride is null || (chatOverride.RepliesEnabled is null && chatOverride.RespondTo is null))
            return global;

        return new ReplyOptions
        {
            Enabled = chatOverride.RepliesEnabled ?? global.Enabled,
            RespondTo = chatOverride.RespondTo ?? global.RespondTo,
            CooldownSeconds = global.CooldownSeconds,
            MaxRepliesPerChatPerHour = global.MaxRepliesPerChatPerHour,
            UseAiWhenNoRuleMatches = global.UseAiWhenNoRuleMatches,
            Rules = global.Rules
        };
    }

    /// <summary>Words banned only inside this chat.</summary>
    public string[] ExtraBannedWords(long chatId) => FindOverride(chatId)?.ExtraBannedWords ?? Array.Empty<string>();

    private ChatOverride? FindOverride(long chatId) => Current.PerChat.FirstOrDefault(c => c.ChatId == chatId);
}
