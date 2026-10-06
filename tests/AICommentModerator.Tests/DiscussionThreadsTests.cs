using AICommentModerator.Application.Bot;
using Xunit;

namespace AICommentModerator.Tests;

public class DiscussionThreadsTests
{
    [Fact]
    public void A_remembered_post_is_found_by_its_thread()
    {
        var threads = new DiscussionThreads();
        threads.Remember(42, 500, "Yangi mahsulot chiqdi");

        Assert.Equal("Yangi mahsulot chiqdi", threads.Find(42, 500));
    }

    [Fact]
    public void Another_chat_does_not_see_it()
    {
        var threads = new DiscussionThreads();
        threads.Remember(42, 500, "post");

        Assert.Null(threads.Find(99, 500));
        Assert.Null(threads.Find(42, 501));
        Assert.Null(threads.Find(42, null));
    }

    [Fact]
    public void A_post_without_text_is_not_remembered()
    {
        var threads = new DiscussionThreads();
        threads.Remember(42, 500, null);
        threads.Remember(42, 501, "   ");

        Assert.Equal(0, threads.Count);
    }

    [Fact]
    public void The_store_stays_bounded()
    {
        var threads = new DiscussionThreads();
        for (var i = 0; i < 700; i++)
            threads.Remember(42, i, $"post {i}");

        Assert.True(threads.Count <= 500, $"count was {threads.Count}");
        Assert.Equal("post 699", threads.Find(42, 699));
    }
}
