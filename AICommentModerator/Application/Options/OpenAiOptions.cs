using System.ComponentModel.DataAnnotations;

namespace AICommentModerator.Application.Options;

public sealed class OpenAiOptions
{
    public const string Section = "OpenAI";

    /// <summary>When empty the service falls back to the rule engine.</summary>
    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4o-mini";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>House rules handed to the model as the system prompt.</summary>
    public string Policy { get; set; } =
        "Block insults, threats, hate speech, sexual content, scams and unsolicited advertising. " +
        "Flag borderline cases such as heated arguments or possible spam. " +
        "Allow ordinary criticism, questions and off-topic small talk.";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
