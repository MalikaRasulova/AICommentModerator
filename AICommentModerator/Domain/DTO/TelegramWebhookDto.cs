using AICommentModerator.Domain.TelegramModels;

namespace AICommentModerator.Domain.DTO
{
    public class TelegramWebhookDto
    {
        public TelegramMessage message { get; set; }
    }

}
