using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Options;
using Microsoft.Extensions.Options;

namespace AICommentModerator.Infrastructure.Telegram;

/// <summary>Thin wrapper over the Bot API calls this service makes.</summary>
public sealed class TelegramClient : ITelegramClient
{
    public const string HttpClientName = "telegram";

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<TelegramOptions> _options;
    private readonly ILogger<TelegramClient> _logger;

    public TelegramClient(HttpClient http, IOptionsMonitor<TelegramOptions> options, ILogger<TelegramClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public Task<bool> SendMessageAsync(long chatId, string text, long? replyToMessageId = null, CancellationToken cancellationToken = default) =>
        CallAsync("sendMessage", new
        {
            chat_id = chatId,
            text,
            reply_to_message_id = replyToMessageId
        }, cancellationToken);

    public Task<bool> DeleteMessageAsync(long chatId, long messageId, CancellationToken cancellationToken = default) =>
        CallAsync("deleteMessage", new
        {
            chat_id = chatId,
            message_id = messageId
        }, cancellationToken);

    public async Task<string?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var token = _options.CurrentValue.BotToken;
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var response = await _http.GetFromJsonAsync<ApiResponse<BotUser>>($"bot{token}/getMe", cancellationToken);
            return response is { Ok: true } ? response.Result?.Username : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Telegram getMe failed");
            return null;
        }
    }

    private async Task<bool> CallAsync(string method, object payload, CancellationToken cancellationToken)
    {
        var token = _options.CurrentValue.BotToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogDebug("Telegram {Method} skipped: no bot token configured", method);
            return false;
        }

        try
        {
            using var response = await _http.PostAsJsonAsync($"bot{token}/{method}", payload, cancellationToken);
            if (response.IsSuccessStatusCode)
                return true;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Telegram {Method} returned {Status}: {Body}", method, (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Telegram {Method} failed", method);
            return false;
        }
    }

    private sealed class ApiResponse<T>
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("result")]
        public T? Result { get; set; }
    }

    private sealed class BotUser
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }
}
