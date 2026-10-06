namespace AICommentModerator.Domain.Telegram;

public class TelegramUser
{
    public long Id { get; set; }

    public bool IsBot { get; set; }

    public string? FirstName { get; set; }

    public string? Username { get; set; }

    public string Display => string.IsNullOrWhiteSpace(Username) ? (FirstName ?? Id.ToString()) : "@" + Username;
}
