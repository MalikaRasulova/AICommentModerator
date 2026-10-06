using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Models;
using AICommentModerator.Application.Moderation;
using AICommentModerator.Application.Options;
using Microsoft.Extensions.Options;

namespace AICommentModerator.Infrastructure.Ai;

/// <summary>
/// Asks an OpenAI chat model for a verdict in strict JSON.
/// The rule engine runs first (a banned word needs no model call) and also catches
/// every failure path: no API key, HTTP error, timeout or unparseable answer.
/// </summary>
public sealed class OpenAiModerationService : IModerationService
{
    public const string SourceName = "openai";
    public const string HttpClientName = "openai";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IOptionsMonitor<OpenAiOptions> _options;
    private readonly RuleBasedModerationService _rules;
    private readonly ILogger<OpenAiModerationService> _logger;

    public OpenAiModerationService(
        HttpClient http,
        IOptionsMonitor<OpenAiOptions> options,
        RuleBasedModerationService rules,
        ILogger<OpenAiModerationService> logger)
    {
        _http = http;
        _options = options;
        _rules = rules;
        _logger = logger;
    }

    public async Task<ModerationResult> ModerateAsync(string text, CancellationToken cancellationToken = default)
    {
        var ruleVerdict = _rules.Evaluate(text);
        if (ruleVerdict.Decision == ModerationDecision.Block)
            return ruleVerdict;

        var options = _options.CurrentValue;
        if (!options.IsConfigured)
            return ruleVerdict;

        try
        {
            var verdict = await AskModelAsync(text, options, cancellationToken);
            if (verdict is not null)
                return Strictest(verdict, ruleVerdict);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "OpenAI call failed; falling back to the rule engine");
        }

        return ruleVerdict;
    }

    private async Task<ModerationResult?> AskModelAsync(string text, OpenAiOptions options, CancellationToken cancellationToken)
    {
        var request = new
        {
            model = options.Model,
            temperature = 0,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        "You moderate chat comments. " + options.Policy + " " +
                        "Answer with JSON only, shaped as " +
                        "{\"decision\":\"allow|flag|block\",\"reason\":\"one short sentence\"," +
                        "\"categories\":[\"...\"],\"confidence\":0.0}. " +
                        "Judge the comment in whatever language it is written in."
                },
                new { role = "user", content = text }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var response = await _http.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OpenAI returned {Status}; falling back to the rule engine",
                (int)response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);
        var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return Parse(content);
    }

    /// <summary>Turns the model answer into a result. Internal so the tests can cover it directly.</summary>
    internal static ModerationResult? Parse(string json)
    {
        ModelVerdict? verdict;
        try
        {
            verdict = JsonSerializer.Deserialize<ModelVerdict>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (verdict is null || string.IsNullOrWhiteSpace(verdict.Decision))
            return null;

        var decision = verdict.Decision.Trim().ToLowerInvariant() switch
        {
            "block" => ModerationDecision.Block,
            "flag" => ModerationDecision.Flag,
            "allow" => ModerationDecision.Allow,
            _ => (ModerationDecision?)null
        };

        if (decision is null)
            return null;

        var confidence = Math.Clamp(verdict.Confidence, 0, 1);
        var reason = string.IsNullOrWhiteSpace(verdict.Reason) ? "No reason given" : verdict.Reason.Trim();

        return new ModerationResult(
            decision.Value,
            reason,
            (IReadOnlyList<string>?)verdict.Categories ?? Array.Empty<string>(),
            confidence,
            SourceName);
    }

    /// <summary>Block beats Flag beats Allow, so a rule hit is never softened by the model.</summary>
    private static ModerationResult Strictest(ModerationResult a, ModerationResult b) =>
        a.Decision >= b.Decision ? a : b;

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }

        internal sealed class Choice
        {
            [JsonPropertyName("message")]
            public ChoiceMessage? Message { get; set; }
        }

        internal sealed class ChoiceMessage
        {
            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }
    }

    internal sealed class ModelVerdict
    {
        [JsonPropertyName("decision")]
        public string? Decision { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }
}
