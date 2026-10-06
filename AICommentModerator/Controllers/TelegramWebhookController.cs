using AICommentModerator.Application.Abstractions;
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
    private readonly ITelegramClient _telegram;
    private readonly IAuditLog _auditLog;
    private readonly IOptionsMonitor<TelegramOptions> _options;
    private readonly ILogger<TelegramWebhookController> _logger;

    public TelegramWebhookController(
        IModerationService moderation,
        ITelegramClient telegram,
        IAuditLog auditLog,
        IOptionsMonitor<TelegramOptions> options,
        ILogger<TelegramWebhookController> logger)
    {
        _moderation = moderation;
        _telegram = telegram;
        _auditLog = auditLog;
        _options = options;
        _logger = logger;
    }

    /// <summary>Receives one Telegram update, moderates the comment and acts on the verdict.</summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> Receive([FromBody] TelegramUpdate update, CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;

        if (!string.IsNullOrWhiteSpace(options.WebhookSecret))
        {
            var provided = Request.Headers[SecretHeader].ToString();
            if (!string.Equals(provided, options.WebhookSecret, StringComparison.Ordinal))
            {
                _logger.LogWarning("Webhook call rejected: wrong or missing secret header");
                return Unauthorized();
            }
        }

        var message = update?.AnyMessage;
        var text = message?.Body;

        // Service messages, stickers and photos without a caption carry nothing to moderate.
        if (message?.Chat is null || string.IsNullOrWhiteSpace(text))
            return Ok(new { status = "ignored" });

        if (message.From?.IsBot == true)
            return Ok(new { status = "ignored", reason = "bot message" });

        var verdict = await _moderation.ModerateAsync(text, cancellationToken);
        var deleted = false;

        if (verdict.Decision == ModerationDecision.Block && options.DeleteBlockedMessages)
        {
            deleted = await _telegram.DeleteMessageAsync(message.Chat.Id, message.MessageId, cancellationToken);

            if (deleted && options.ReplyOnBlock)
            {
                await _telegram.SendMessageAsync(
                    message.Chat.Id,
                    "A comment was removed: " + verdict.Reason,
                    cancellationToken: cancellationToken);
            }
        }

        await _auditLog.RecordAsync(
            "telegram",
            message.Chat.Id,
            message.MessageId,
            message.From?.Display,
            text,
            verdict,
            deleted,
            cancellationToken);

        _logger.LogInformation(
            "Comment {MessageId} in chat {ChatId}: {Decision} by {Source} ({Reason})",
            message.MessageId, message.Chat.Id, verdict.Decision, verdict.Source, verdict.Reason);

        return Ok(new
        {
            status = "processed",
            decision = verdict.Decision.ToString().ToLowerInvariant(),
            source = verdict.Source,
            reason = verdict.Reason,
            deleted
        });
    }

    /// <summary>Runs a comment through the moderator without touching Telegram. Handy for trying the rules out.</summary>
    [HttpPost("check")]
    public async Task<ActionResult<ModerationResult>> Check([FromBody] CheckRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "text is required" });

        var verdict = await _moderation.ModerateAsync(request.Text, cancellationToken);
        return Ok(verdict);
    }

    public sealed record CheckRequest(string Text);
}
