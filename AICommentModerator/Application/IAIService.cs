namespace AICommentModerator.Application
{
    public interface IAIService
    {
        Task<string> GenerateResponseAsync(string comment);
    }
}
