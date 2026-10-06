using System.ComponentModel.DataAnnotations;

namespace AICommentModerator.Application.Options;

/// <summary>
/// Everything about how this bot behaves in a chat, loaded from bot.config.json.
/// The file is watched, so edits take effect without a restart.
/// </summary>
public sealed class BotOptions
{
    public const string Section = "Bot";

    /// <summary>Bot username without the @. Left empty it is read from Telegram at startup.</summary>
    public string? Username { get; set; }

    public ScopeOptions Scope { get; set; } = new();

    public ExemptOptions Exempt { get; set; } = new();

    public ActionOptions Actions { get; set; } = new();

    public ModeratorOptions Moderators { get; set; } = new();

    public ReplyOptions Replies { get; set; } = new();

    /// <summary>Per-chat overrides; the first entry matching the chat id wins.</summary>
    public List<ChatOverride> PerChat { get; set; } = new();
}

/// <summary>Where the bot is allowed to work.</summary>
public sealed class ScopeOptions
{
    /// <summary>"AllChats" or "Allowlist".</summary>
    public string Mode { get; set; } = "AllChats";

    public long[] AllowedChatIds { get; set; } = Array.Empty<long>();

    /// <summary>Never moderate what another bot posted.</summary>
    public bool IgnoreBots { get; set; } = true;

    /// <summary>Skip channel posts (the channel owner's own publications).</summary>
    public bool IgnoreChannelPosts { get; set; } = true;

    /// <summary>Skip a message that was only edited after it passed once.</summary>
    public bool IgnoreEditedMessages { get; set; }

    public bool IsAllowlist => string.Equals(Mode, "Allowlist", StringComparison.OrdinalIgnoreCase);
}

/// <summary>People the bot never acts against.</summary>
public sealed class ExemptOptions
{
    /// <summary>Usernames without the @.</summary>
    public string[] Usernames { get; set; } = Array.Empty<string>();

    public long[] UserIds { get; set; } = Array.Empty<long>();

    /// <summary>When false an exempt author is still moderated, only nothing is deleted.</summary>
    public bool SkipModeration { get; set; } = true;
}

/// <summary>What happens after each verdict.</summary>
public sealed class ActionOptions
{
    public DecisionActions OnBlock { get; set; } = new() { Delete = true, NotifyModerators = true, WarnAuthor = true };

    public DecisionActions OnFlag { get; set; } = new() { NotifyModerators = true };

    public DecisionActions OnAllow { get; set; } = new();
}

public sealed class DecisionActions
{
    /// <summary>Remove the message from the chat.</summary>
    public bool Delete { get; set; }

    /// <summary>Reply in the chat where it happened, quoting nothing.</summary>
    public bool WarnAuthor { get; set; }

    /// <summary>Forward a short note to the moderators chat.</summary>
    public bool NotifyModerators { get; set; }
}

public sealed class ModeratorOptions
{
    /// <summary>Chat that receives notifications. 0 turns notifications off.</summary>
    public long ChatId { get; set; }

    /// <summary>Placeholders: {decision} {reason} {author} {chat} {text}</summary>
    public string Template { get; set; } = "{decision}: {reason}\nAuthor: {author}\nText: {text}";

    /// <summary>Text posted in the chat when a comment is removed. Placeholder: {reason}</summary>
    public string WarningTemplate { get; set; } = "A comment was removed: {reason}";

    public bool IsConfigured => ChatId != 0;
}

/// <summary>Who gets an answer, and what the answer is.</summary>
public sealed class ReplyOptions
{
    public bool Enabled { get; set; }

    /// <summary>"Nobody", "MentionsAndReplies" or "Everyone".</summary>
    public string RespondTo { get; set; } = "MentionsAndReplies";

    /// <summary>Quietest gap between two answers to the same person in the same chat.</summary>
    [Range(0, 3600)]
    public int CooldownSeconds { get; set; } = 30;

    /// <summary>Hard ceiling so a busy chat cannot be flooded.</summary>
    [Range(0, 1000)]
    public int MaxRepliesPerChatPerHour { get; set; } = 20;

    /// <summary>Let the model answer when no rule matched. Needs an OpenAI key.</summary>
    public bool UseAiWhenNoRuleMatches { get; set; }

    /// <summary>Checked in order; the first match wins.</summary>
    public List<ReplyRule> Rules { get; set; } = new();

    public ReplyAudience Audience => RespondTo?.Trim().ToLowerInvariant() switch
    {
        "everyone" => ReplyAudience.Everyone,
        "mentionsandreplies" => ReplyAudience.MentionsAndReplies,
        _ => ReplyAudience.Nobody
    };
}

public enum ReplyAudience
{
    Nobody,
    MentionsAndReplies,
    Everyone
}

/// <summary>One canned answer and what triggers it.</summary>
public sealed class ReplyRule
{
    public string Name { get; set; } = string.Empty;

    /// <summary>"Keyword" (whole word, case-insensitive) or "Regex".</summary>
    public string Match { get; set; } = "Keyword";

    public string[] Patterns { get; set; } = Array.Empty<string>();

    public string Reply { get; set; } = string.Empty;

    /// <summary>Answer only these usernames (without @). Empty means anyone.</summary>
    public string[] OnlyForUsernames { get; set; } = Array.Empty<string>();

    public bool IsRegex => string.Equals(Match, "Regex", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Settings that replace the defaults inside one chat.</summary>
public sealed class ChatOverride
{
    public long ChatId { get; set; }

    /// <summary>false switches the bot off in this chat without removing it.</summary>
    public bool? Enabled { get; set; }

    public bool? RepliesEnabled { get; set; }

    public string? RespondTo { get; set; }

    /// <summary>Words banned in this chat on top of the global list.</summary>
    public string[] ExtraBannedWords { get; set; } = Array.Empty<string>();
}
