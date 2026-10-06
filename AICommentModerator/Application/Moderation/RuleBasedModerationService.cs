using System.Text.RegularExpressions;
using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Models;
using AICommentModerator.Application.Options;
using Microsoft.Extensions.Options;

namespace AICommentModerator.Application.Moderation;

/// <summary>
/// Deterministic checks that need no API key: banned words, link spam, shouting and noise.
/// It is both the fallback when the model is unavailable and the first, cheap pass.
/// </summary>
public sealed class RuleBasedModerationService : IModerationService
{
    public const string SourceName = "rules";

    private static readonly Regex LinkPattern = new(
        @"(https?://|www\.)\S+|\b[a-z0-9-]+\.(com|net|org|uz|ru|io|me|xyz|top)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InvitePattern = new(
        @"t\.me/|telegram\.me/|@[a-z0-9_]{5,32}\s+(kanal|channel|obuna)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IOptionsMonitor<ModerationOptions> _options;

    public RuleBasedModerationService(IOptionsMonitor<ModerationOptions> options) => _options = options;

    public Task<ModerationResult> ModerateAsync(
        string text,
        IReadOnlyCollection<string>? extraBannedWords = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Evaluate(text, extraBannedWords));

    public ModerationResult Evaluate(string text, IReadOnlyCollection<string>? extraBannedWords = null)
    {
        var options = _options.CurrentValue;
        var banned = extraBannedWords is { Count: > 0 }
            ? options.BannedWords.Concat(extraBannedWords).ToArray()
            : options.BannedWords;

        if (string.IsNullOrWhiteSpace(text))
            return ModerationResult.Allowed(SourceName, "Empty comment");

        var blocked = new List<string>();
        var flagged = new List<string>();

        if (ContainsAny(text, banned, out var bannedHit))
            blocked.Add($"banned-word:{bannedHit}");

        if (text.Length > options.MaxLength)
            blocked.Add("too-long");

        var linkCount = LinkPattern.Matches(text).Count;
        if (linkCount > options.MaxLinks)
            blocked.Add($"link-spam:{linkCount}");
        else if (linkCount > 0 && InvitePattern.IsMatch(text))
            flagged.Add("channel-promotion");

        if (ContainsAny(text, options.SuspiciousWords, out var suspiciousHit))
            flagged.Add($"suspicious-word:{suspiciousHit}");

        if (IsShouting(text, options))
            flagged.Add("shouting");

        if (HasCharacterRun(text, options.MaxRepeatedChars))
            flagged.Add("repeated-characters");

        if (blocked.Count > 0)
            return new ModerationResult(
                ModerationDecision.Block,
                "Rule violation: " + string.Join(", ", blocked),
                blocked,
                1.0,
                SourceName);

        if (flagged.Count > 0)
            return new ModerationResult(
                ModerationDecision.Flag,
                "Needs a human look: " + string.Join(", ", flagged),
                flagged,
                0.6,
                SourceName);

        return ModerationResult.Allowed(SourceName);
    }

    private static bool ContainsAny(string text, IReadOnlyCollection<string> words, out string match)
    {
        foreach (var word in words)
        {
            if (string.IsNullOrWhiteSpace(word))
                continue;

            var pattern = $@"(^|[^\p{{L}}\p{{N}}]){Regex.Escape(word.Trim())}($|[^\p{{L}}\p{{N}}])";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                match = word.Trim();
                return true;
            }
        }

        match = string.Empty;
        return false;
    }

    private static bool IsShouting(string text, ModerationOptions options)
    {
        var letters = text.Where(char.IsLetter).ToArray();
        if (letters.Length < options.ShoutingMinLength)
            return false;

        var upper = letters.Count(char.IsUpper);
        return (double)upper / letters.Length > options.MaxUppercaseRatio;
    }

    private static bool HasCharacterRun(string text, int limit)
    {
        var run = 1;
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == text[i - 1] && !char.IsWhiteSpace(text[i]))
            {
                if (++run >= limit)
                    return true;
            }
            else
            {
                run = 1;
            }
        }

        return false;
    }
}
