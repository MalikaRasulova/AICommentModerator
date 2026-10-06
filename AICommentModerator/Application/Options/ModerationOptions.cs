using System.ComponentModel.DataAnnotations;

namespace AICommentModerator.Application.Options;

public sealed class ModerationOptions
{
    public const string Section = "Moderation";

    /// <summary>Words that always mean Block. Matched whole-word, case-insensitive.</summary>
    public string[] BannedWords { get; set; } = Array.Empty<string>();

    /// <summary>Words that only raise a Flag.</summary>
    public string[] SuspiciousWords { get; set; } = Array.Empty<string>();

    /// <summary>More links than this in one comment counts as spam.</summary>
    [Range(0, 50)]
    public int MaxLinks { get; set; } = 2;

    /// <summary>Share of CAPITAL letters that counts as shouting (0..1).</summary>
    [Range(0, 1)]
    public double MaxUppercaseRatio { get; set; } = 0.7;

    /// <summary>Comments shorter than this are never judged as shouting.</summary>
    [Range(1, 200)]
    public int ShoutingMinLength { get; set; } = 12;

    /// <summary>Longest run of one repeated character before it reads as noise.</summary>
    [Range(2, 50)]
    public int MaxRepeatedChars { get; set; } = 8;

    [Range(1, 10000)]
    public int MaxLength { get; set; } = 4000;
}
