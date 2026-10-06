using System.Collections.Concurrent;

namespace AICommentModerator.Application.Bot;

/// <summary>
/// Remembers the channel post each comment thread hangs under, so a generated answer can
/// talk about the post instead of guessing. Bounded and in-memory: losing it on restart
/// only costs the bot some context, never a comment.
/// </summary>
public sealed class DiscussionThreads
{
    private const int Capacity = 500;

    private readonly ConcurrentDictionary<(long ChatId, long ThreadId), string> _posts = new();
    private readonly ConcurrentQueue<(long ChatId, long ThreadId)> _order = new();

    public void Remember(long chatId, long threadId, string? postText)
    {
        if (string.IsNullOrWhiteSpace(postText))
            return;

        var key = (chatId, threadId);
        if (_posts.TryAdd(key, postText.Trim()))
            _order.Enqueue(key);

        while (_order.Count > Capacity && _order.TryDequeue(out var oldest))
            _posts.TryRemove(oldest, out _);
    }

    public string? Find(long chatId, long? threadId) =>
        threadId is not null && _posts.TryGetValue((chatId, threadId.Value), out var text) ? text : null;

    public int Count => _posts.Count;
}
