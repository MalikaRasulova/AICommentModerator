namespace AICommentModerator.Application
{
    public interface ILoggerService
    {
        Task LogInteractionAsync(string platform, string input, string aiOutput);
        Task LogModerationAsync(string moderator, string finalText, string actionType);
    }
}
