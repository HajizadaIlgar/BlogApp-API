using BlogApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogApp.DAL.DALs;

public class BlogAppDbContext : DbContext
{
    public BlogAppDbContext(DbContextOptions opt) : base(opt) { }
    public DbSet<Category> Categories { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BlogAppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);

        // ChatMessage konfiqurasiyası
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Sender)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Receiver)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Content)
                .HasMaxLength(2000);

            entity.Property(e => e.ImageUrl)
                .HasMaxLength(500);

            entity.Property(e => e.Type)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue("text");

            entity.Property(e => e.Timestamp)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");

            entity.Property(e => e.IsRead)
                .IsRequired()
                .HasDefaultValue(false);

            // Indexlər - performans üçün
            entity.HasIndex(e => e.Sender)
                .HasDatabaseName("IX_ChatMessages_Sender");

            entity.HasIndex(e => e.Receiver)
                .HasDatabaseName("IX_ChatMessages_Receiver");

            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_ChatMessages_Timestamp");

            entity.HasIndex(e => new { e.Sender, e.Receiver })
                .HasDatabaseName("IX_ChatMessages_Sender_Receiver");
        });
    }
}
