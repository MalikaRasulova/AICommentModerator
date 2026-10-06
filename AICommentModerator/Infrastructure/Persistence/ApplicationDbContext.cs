using AICommentModerator.Domain;
using Microsoft.EntityFrameworkCore;

namespace AICommentModerator.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<ModerationAction> ModerationActions => Set<ModerationAction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Comment>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Platform).HasMaxLength(32).IsRequired();
            entity.Property(c => c.Author).HasMaxLength(128);
            entity.Property(c => c.Text).HasMaxLength(4096).IsRequired();
            entity.HasIndex(c => c.ReceivedAt);
            entity.HasIndex(c => new { c.ChatId, c.MessageId });

            entity.HasOne(c => c.Action)
                  .WithOne(a => a.Comment!)
                  .HasForeignKey<ModerationAction>(a => a.CommentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModerationAction>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Decision).HasMaxLength(16).IsRequired();
            entity.Property(a => a.Reason).HasMaxLength(512).IsRequired();
            entity.Property(a => a.Categories).HasMaxLength(512);
            entity.Property(a => a.Source).HasMaxLength(32).IsRequired();
            entity.HasIndex(a => a.DecidedAt);
            entity.HasIndex(a => a.Decision);
        });
    }
}
