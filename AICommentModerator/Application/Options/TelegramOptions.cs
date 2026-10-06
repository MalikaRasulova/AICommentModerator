using System.ComponentModel.DataAnnotations;

namespace AICommentModerator.Application.Options;

public sealed class TelegramOptions
{
    public const string Section = "Telegram";

    /// <summary>Bot token from BotFather. Without it the service only logs decisions.</summary>
    public string? BotToken { get; set; }

    /// <summary>
    /// Value Telegram must send back in the X-Telegram-Bot-Api-Secret-Token header.
    /// Set it when registering the webhook; requests without it are rejected.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Delete the message when the verdict is Block.</summary>
    public bool DeleteBlockedMessages { get; set; } = true;

    /// <summary>Reply in the chat explaining why a comment was removed.</summary>
    public bool ReplyOnBlock { get; set; }

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}
