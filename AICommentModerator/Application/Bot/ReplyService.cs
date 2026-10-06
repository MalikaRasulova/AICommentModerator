using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Options;

namespace AICommentModerator.Application.Bot;

/// <summary>
/// Matches a comment against the reply rules, keeps the bot from repeating itself
/// and, when the config allows it, hands anything unmatched to the model.
/// </summary>
public sealed class ReplyService : IReplyService
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly BotPolicy _policy;
    private readonly IReplyGenerator? _generator;
    private readonly ILogger<ReplyService> _logger;

    private readonly ConcurrentDictionary<(long Chat, long User), DateTimeOffset> _lastReply = new();
    private readonly ConcurrentDictionary<long, List<DateTimeOffset>> _chatHistory = new();

    public ReplyService(BotPolicy policy, ILogger<ReplyService> logger, IReplyGenerator? generator = null)
    {
        _policy = policy;
        _logger = logger;
        _generator = generator;
    }

    /// <summary>Overridable clock so the tests do not have to wait for a cooldown.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;

    public async Task<string?> TryGetReplyAsync(ReplyContext context, CancellationToken cancellationToken = default)
    {
        var options = _policy.RepliesFor(context.ChatId);

        if (!options.Enabled || options.Audience == ReplyAudience.Nobody)
            return null;

        if (options.Audience == ReplyAudience.MentionsAndReplies && !context.MentionsBot && !context.IsReplyToBot)
            return null;

        if (IsOnCooldown(context, options) || ReachedHourlyLimit(context.ChatId, options))
            return null;

        var rule = FindRule(options, context);
        if (rule is not null)
        {
            Remember(context);
            return rule.Reply;
        }

        if (!options.UseAiWhenNoRuleMatches || _generator is null)
            return null;

        try
        {
            var generated = await _generator.GenerateAsync(context.Text, cancellationToken);
            if (string.IsNullOrWhiteSpace(generated))
                return null;

            Remember(context);
            return generated;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not generate a reply; staying quiet");
            return null;
        }
    }

    private static ReplyRule? FindRule(ReplyOptions options, ReplyContext context)
    {
        foreach (var rule in options.Rules)
        {
            if (rule.OnlyForUsernames.Length > 0)
            {
                var username = context.Username?.TrimStart('@');
                if (string.IsNullOrWhiteSpace(username) ||
                    !rule.OnlyForUsernames.Any(u => string.Equals(u.TrimStart('@'), username, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            if (Matches(rule, context.Text))
                return rule;
        }

        return null;
    }

    private static bool Matches(ReplyRule rule, string text)
    {
        foreach (var pattern in rule.Patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                continue;

            try
            {
                var expression = rule.IsRegex
                    ? pattern
                    : $@"(^|[^\p{{L}}\p{{N}}]){Regex.Escape(pattern.Trim())}($|[^\p{{L}}\p{{N}}])";

                if (Regex.IsMatch(text, expression, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                    return true;
            }
            catch (ArgumentException)
            {
                // A broken regex in the config must not take the bot down.
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        return false;
    }

    private bool IsOnCooldown(ReplyContext context, ReplyOptions options)
    {
        if (options.CooldownSeconds <= 0)
            return false;

        if (!_lastReply.TryGetValue((context.ChatId, context.UserId), out var last))
            return false;

        return Now() - last < TimeSpan.FromSeconds(options.CooldownSeconds);
    }

    private bool ReachedHourlyLimit(long chatId, ReplyOptions options)
    {
        if (options.MaxRepliesPerChatPerHour <= 0)
            return false;

        var history = _chatHistory.GetOrAdd(chatId, _ => new List<DateTimeOffset>());
        lock (history)
        {
            var cutoff = Now() - Hour;
            history.RemoveAll(t => t < cutoff);
            return history.Count >= options.MaxRepliesPerChatPerHour;
        }
    }

    private void Remember(ReplyContext context)
    {
        var now = Now();
        _lastReply[(context.ChatId, context.UserId)] = now;

        var history = _chatHistory.GetOrAdd(context.ChatId, _ => new List<DateTimeOffset>());
        lock (history)
        {
            history.Add(now);
        }
    }
}
