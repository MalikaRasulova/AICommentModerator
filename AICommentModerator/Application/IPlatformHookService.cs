namespace AICommentModerator.Application
{
    public interface IPlatformHookService
    {
        Task ReceiveCommentAsync(string payload);
    }
}
