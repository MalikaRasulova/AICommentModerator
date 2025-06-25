using AICommentModerator.Application;
using AICommentModerator.Domain;
using AICommentModerator.Infrastructure.Data;


namespace AICommentModerator.Infrastructure.Services
{
    public class LoggerService : ILoggerService
    {
        private readonly ApplicationDbContext _context;

        public LoggerService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogInteractionAsync(string platform, string input, string aiOutput)
        {
            var comment = new Comment
            {
                Id = Guid.NewGuid(),
                Platform = platform,
                Text = input,
                ReceivedAt = DateTime.UtcNow
            };

            var response = new AIResponse
            {
                Id = Guid.NewGuid(),
                CommentId = comment.Id,
                GeneratedText = aiOutput,
                GeneratedAt = DateTime.UtcNow
            };

            _context.Comments.Add(comment);
            _context.AIResponses.Add(response);
            await _context.SaveChangesAsync();
        }

        public async Task LogModerationAsync(string moderator, string finalText, string actionType)
        {
            var action = new ModerationAction
            {
                Id = Guid.NewGuid(),
                Moderator = moderator,
                FinalText = finalText,
                ActionType = actionType,
                Timestamp = DateTime.UtcNow
            };

            _context.ModerationActions.Add(action);
            await _context.SaveChangesAsync();
        }
    }
}
