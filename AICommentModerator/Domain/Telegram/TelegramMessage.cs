namespace AICommentModerator.Domain.Telegram;

public class TelegramMessage
{
    public long MessageId { get; set; }

    public TelegramUser? From { get; set; }

    public TelegramChat? Chat { get; set; }

    public string? Text { get; set; }

    public string? Caption { get; set; }

    /// <summary>Telegram puts photo captions in a separate field; treat both as the comment body.</summary>
    public string? Body => string.IsNullOrWhiteSpace(Text) ? Caption : Text;
}
