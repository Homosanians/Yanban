using Microsoft.EntityFrameworkCore;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Persistence;

public class YanbanDbContext : DbContext
{
    public YanbanDbContext(DbContextOptions<YanbanDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Board> Boards => Set<Board>();
    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
    public DbSet<BoardList> Lists => Set<BoardList>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(YanbanDbContext).Assembly);
    }
}
