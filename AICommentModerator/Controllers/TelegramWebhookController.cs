using AICommentModerator.Application.Abstractions;
using AICommentModerator.Application.Bot;
using AICommentModerator.Application.Models;
using AICommentModerator.Application.Options;
using AICommentModerator.Domain.Telegram;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AICommentModerator.Controllers;

[ApiController]
[Route("api/telegram")]
public class TelegramWebhookController : ControllerBase
{
    /// <summary>Header Telegram sends when the webhook was registered with a secret token.</summary>
    public const string SecretHeader = "X-Telegram-Bot-Api-Secret-Token";

    private readonly IModerationService _moderation;
    private readonly IReplyService _replies;
    private readonly ITelegramClient _telegram;
    private readonly IAuditLog _auditLog;
    private readonly BotPolicy _policy;
    private readonly IOptionsMonitor<TelegramOptions> _telegramOptions;
    private readonly ILogger<TelegramWebhookController> _logger;

    public TelegramWebhookController(
        IModerationService moderation,
        IReplyService replies,
        ITelegramClient telegram,
        IAuditLog auditLog,
        BotPolicy policy,
        IOptionsMonitor<TelegramOptions> telegramOptions,
        ILogger<TelegramWebhookController> logger)
    {
        _moderation = moderation;
        _replies = replies;
        _telegram = telegram;
        _auditLog = auditLog;
        _policy = policy;
        _telegramOptions = telegramOptions;
        _logger = logger;
    }

    /// <summary>Receives one Telegram update, moderates the comment and acts on the verdict.</summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Receive([FromBody] TelegramUpdate update, CancellationToken cancellationToken)
    {
        var telegramOptions = _telegramOptions.CurrentValue;

        if (!string.IsNullOrWhiteSpace(telegramOptions.WebhookSecret))
        {
            var provided = Request.Headers[SecretHeader].ToString();
            if (!string.Equals(provided, telegramOptions.WebhookSecret, StringComparison.Ordinal))
            {
                _logger.LogWarning("Webhook call rejected: wrong or missing secret header");
                return Unauthorized();
            }
        }

        var bot = _policy.Current;

        if (update is null)
            return Ok(new { status = "ignored", reason = "empty update" });

        if (bot.Scope.IgnoreChannelPosts && (update.ChannelPost is not null || update.EditedChannelPost is not null))
            return Ok(new { status = "ignored", reason = "channel post" });

        if (bot.Scope.IgnoreEditedMessages && (update.EditedMessage is not null || update.EditedChannelPost is not null))
            return Ok(new { status = "ignored", reason = "edited message" });

        var message = update.AnyMessage;
        var text = message?.Body;

        // Service messages, stickers and photos without a caption carry nothing to moderate.
        if (message?.Chat is null || string.IsNullOrWhiteSpace(text))
            return Ok(new { status = "ignored", reason = "no text" });

        if (bot.Scope.IgnoreBots && message.From?.IsBot == true)
            return Ok(new { status = "ignored", reason = "bot message" });

        var chatId = message.Chat.Id;
        if (!_policy.HandlesChat(chatId))
            return Ok(new { status = "ignored", reason = "chat out of scope" });

        var authorId = message.From?.Id ?? 0;
        var username = message.From?.Username;
        var exempt = _policy.IsExempt(authorId, username);

        ModerationResult verdict;
        var deleted = false;

        if (exempt && bot.Exempt.SkipModeration)
        {
            verdict = ModerationResult.Allowed("exempt", "Author is on the exempt list");
        }
        else
        {
            verdict = await _moderation.ModerateAsync(text, _policy.ExtraBannedWords(chatId), cancellationToken);
            var actions = _policy.ActionsFor(verdict.Decision);

            if (!exempt && actions.Delete && telegramOptions.DeleteBlockedMessages)
                deleted = await _telegram.DeleteMessageAsync(chatId, message.MessageId, cancellationToken);

            if (!exempt && actions.WarnAuthor && deleted)
            {
                var warning = bot.Moderators.WarningTemplate.Replace("{reason}", verdict.Reason);
                await _telegram.SendMessageAsync(chatId, warning, cancellationToken: cancellationToken);
            }

            if (actions.NotifyModerators && bot.Moderators.IsConfigured)
                await NotifyModeratorsAsync(bot, message, text, verdict, cancellationToken);
        }

        await _auditLog.RecordAsync(
            "telegram",
            chatId,
            message.MessageId,
            message.From?.Display,
            text,
            verdict,
            deleted,
            cancellationToken);

        var replied = false;
        if (verdict.Decision != ModerationDecision.Block)
            replied = await TryReplyAsync(bot, message, text, cancellationToken);

        _logger.LogInformation(
            "Comment {MessageId} in chat {ChatId}: {Decision} by {Source} ({Reason})",
            message.MessageId, chatId, verdict.Decision, verdict.Source, verdict.Reason);

        return Ok(new
        {
            status = "processed",
            decision = verdict.Decision.ToString().ToLowerInvariant(),
            source = verdict.Source,
            reason = verdict.Reason,
            deleted,
            replied
        });
    }

    private async Task<bool> TryReplyAsync(BotOptions bot, TelegramMessage message, string text, CancellationToken cancellationToken)
    {
        var botUsername = bot.Username?.TrimStart('@');
        var mentionsBot = !string.IsNullOrWhiteSpace(botUsername) &&
                          text.Contains("@" + botUsername, StringComparison.OrdinalIgnoreCase);

        var context = new ReplyContext(
            message.Chat!.Id,
            message.From?.Id ?? 0,
            message.From?.Username,
            text,
            mentionsBot,
            message.ReplyToMessage?.From?.IsBot == true);

        var reply = await _replies.TryGetReplyAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(reply))
            return false;

        return await _telegram.SendMessageAsync(message.Chat.Id, reply, message.MessageId, cancellationToken);
    }

    private Task NotifyModeratorsAsync(
        BotOptions bot,
        TelegramMessage message,
        string text,
        ModerationResult verdict,
        CancellationToken cancellationToken)
    {
        var note = bot.Moderators.Template
            .Replace("{decision}", verdict.Decision.ToString())
            .Replace("{reason}", verdict.Reason)
            .Replace("{author}", message.From?.Display ?? "unknown")
            .Replace("{chat}", message.Chat?.Title ?? message.Chat?.Id.ToString() ?? "unknown")
            .Replace("{text}", text.Length > 300 ? text[..300] + "..." : text);

        return _telegram.SendMessageAsync(bot.Moderators.ChatId, note, cancellationToken: cancellationToken);
    }

    /// <summary>Runs a comment through the moderator without touching Telegram. Handy for trying the rules out.</summary>
    [HttpPost("check")]
    public async Task<ActionResult<ModerationResult>> Check([FromBody] CheckRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "text is required" });

        var verdict = await _moderation.ModerateAsync(request.Text, cancellationToken: cancellationToken);
        return Ok(verdict);
    }

    public sealed record CheckRequest(string Text);
}
