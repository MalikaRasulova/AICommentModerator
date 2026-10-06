namespace AICommentModerator.Domain.Telegram;

public class TelegramMessage
{
    public long MessageId { get; set; }

    public TelegramUser? From { get; set; }

    /// <summary>Set when a channel posted on its own behalf - including the copy that lands
    /// in the linked discussion group.</summary>
    public TelegramChat? SenderChat { get; set; }

    public TelegramChat? Chat { get; set; }

    /// <summary>True on the copy Telegram pushes into the discussion group for a channel post.</summary>
    public bool IsAutomaticForward { get; set; }

    /// <summary>Comments under one post share a thread id - the id of the forwarded post.</summary>
    public long? MessageThreadId { get; set; }

    public string? Text { get; set; }

    public string? Caption { get; set; }

    /// <summary>Set when this message answers another one - that is how a reply to the bot is spotted.</summary>
    public TelegramMessage? ReplyToMessage { get; set; }

    /// <summary>Telegram puts photo captions in a separate field; treat both as the comment body.</summary>
    public string? Body => string.IsNullOrWhiteSpace(Text) ? Caption : Text;

    /// <summary>
    /// The thread this message belongs to: its own thread id, or the post it answers.
    /// Comments under a channel post all resolve to the same value.
    /// </summary>
    public long? ThreadId => MessageThreadId ?? ReplyToMessage?.MessageId;
}
