namespace AICommentModerator.Application.Abstractions;

/// <summary>The slice of the Telegram Bot API this service needs.</summary>
public interface ITelegramClient
{
    Task<bool> SendMessageAsync(long chatId, string text, long? replyToMessageId = null, CancellationToken cancellationToken = default);

    Task<bool> DeleteMessageAsync(long chatId, long messageId, CancellationToken cancellationToken = default);

    /// <summary>Returns the bot username, or null when the token is missing or rejected.</summary>
    Task<string?> GetMeAsync(CancellationToken cancellationToken = default);
}
