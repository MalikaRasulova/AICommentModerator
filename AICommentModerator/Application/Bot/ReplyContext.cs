namespace AICommentModerator.Application.Bot;

/// <summary>Everything the reply rules need to know about one incoming comment.</summary>
/// <param name="ChatId">Chat the comment was posted in.</param>
/// <param name="UserId">Author id, used for the per-person cooldown.</param>
/// <param name="Username">Author username without the @, when there is one.</param>
/// <param name="Text">The comment itself.</param>
/// <param name="MentionsBot">The comment contains @thebot.</param>
/// <param name="IsReplyToBot">The comment answers one of the bot's own messages.</param>
/// <param name="IsUnderChannelPost">The comment sits in a thread under a channel post.</param>
/// <param name="PostText">The channel post being commented on, when it is known.</param>
public sealed record ReplyContext(
    long ChatId,
    long UserId,
    string? Username,
    string Text,
    bool MentionsBot,
    bool IsReplyToBot,
    bool IsUnderChannelPost = false,
    string? PostText = null);
