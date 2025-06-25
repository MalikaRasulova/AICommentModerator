namespace AICommentModerator.Domain.TelegramModels
{
    public class TelegramMessage
    {
        public long message_id { get; set; }
        public TelegramUser from { get; set; }
        public TelegramChat chat { get; set; }
        public string text { get; set; }
    }
}
