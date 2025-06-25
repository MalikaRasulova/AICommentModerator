using AICommentModerator.Application;
using AICommentModerator.Domain.DTO;
using Microsoft.AspNetCore.Mvc;

namespace AICommentModerator.Controllers
{
    [ApiController]
    [Route("api/telegram")]
    public class TelegramWebhookController : ControllerBase
    {
        private readonly IAIService _aiService;
        private readonly ILoggerService _logger;
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;

        public TelegramWebhookController(IAIService aiService, ILoggerService logger, IConfiguration config, HttpClient httpClient)
        {
            _aiService = aiService;
            _logger = logger;
            _config = config;
            _httpClient = httpClient;
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Receive([FromBody] TelegramWebhookDto input)
        {
            var message = input.message;
            if (message?.text == null)
                return Ok();

            var aiReply = await _aiService.GenerateResponseAsync(message.text);
            await _logger.LogInteractionAsync("telegram", message.text, aiReply);

            await SendMessageToTelegram(message.chat.id, aiReply);
            return Ok();
        }

        private async Task SendMessageToTelegram(long chatId, string text)
        {
            var token = _config["Telegram:BotToken"];
            var url = $"https://api.telegram.org/bot{token}/sendMessage";

            var payload = new
            {
                chat_id = chatId,
                text = text
            };

            await _httpClient.PostAsJsonAsync(url, payload);
        }
    }
}
