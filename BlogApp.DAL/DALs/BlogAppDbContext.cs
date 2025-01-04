using BlogApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogApp.DAL.DALs;

public class BlogAppDbContext : DbContext
{
    public BlogAppDbContext(DbContextOptions opt) : base(opt) { }
    public DbSet<Category> Categories { get; set; }
    public DbSet<User> Users { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BlogAppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
