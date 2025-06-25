using AICommentModerator.Domain;
using Microsoft.EntityFrameworkCore;


namespace AICommentModerator.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Comment> Comments { get; set; }
        public DbSet<AIResponse> AIResponses { get; set; }
        public DbSet<ModerationAction> ModerationActions { get; set; }
    }
}
