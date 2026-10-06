using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Options;
using Microsoft.Extensions.Options;

namespace AICommentModerator.Infrastructure.Ai;

/// <summary>Writes a short answer to a comment no reply rule covered.</summary>
public sealed class OpenAiReplyGenerator : IReplyGenerator
{
    public const string HttpClientName = "openai-replies";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<OpenAiOptions> _options;
    private readonly ILogger<OpenAiReplyGenerator> _logger;

    public OpenAiReplyGenerator(HttpClient http, IOptionsMonitor<OpenAiOptions> options, ILogger<OpenAiReplyGenerator> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> GenerateAsync(string comment, string? postText = null, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (!options.IsConfigured)
            return null;

        var messages = new List<object> { new { role = "system", content = options.ReplyPrompt } };

        if (!string.IsNullOrWhiteSpace(postText))
        {
            // The comment is an answer to a post; the model needs to see what was posted.
            messages.Add(new
            {
                role = "system",
                content = "The post being commented on:\n" + Shorten(postText, 1500)
            });
        }

        messages.Add(new { role = "user", content = comment });

        var request = new
        {
            model = options.Model,
            temperature = 0.4,
            max_tokens = 200,
            messages = messages.ToArray()
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        try
        {
            using var response = await _http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenAI reply call returned {Status}", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<Completion>(JsonOptions, cancellationToken);
            var text = payload?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Could not reach OpenAI for a reply");
            return null;
        }
    }

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";

    private sealed class Completion
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }

        internal sealed class Choice
        {
            [JsonPropertyName("message")]
            public Body? Message { get; set; }
        }

        internal sealed class Body
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }
    }
}
