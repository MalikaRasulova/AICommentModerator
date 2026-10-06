namespace AICommentModerator.Domain.Telegram;

/// <summary>Root object Telegram posts to the webhook.</summary>
public class TelegramUpdate
{
    public long UpdateId { get; set; }

    public TelegramMessage? Message { get; set; }

    public TelegramMessage? EditedMessage { get; set; }

    public TelegramMessage? ChannelPost { get; set; }

    public TelegramMessage? EditedChannelPost { get; set; }

    /// <summary>The first message this update actually carries, if any.</summary>
    public TelegramMessage? AnyMessage => Message ?? EditedMessage ?? ChannelPost ?? EditedChannelPost;
}
