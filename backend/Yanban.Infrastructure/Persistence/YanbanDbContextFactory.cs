using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Yanban.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef` can build the model / generate migrations
/// without booting the API host. Not used at runtime.
/// </summary>
public class YanbanDbContextFactory : IDesignTimeDbContextFactory<YanbanDbContext>
{
    public YanbanDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("YANBAN_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=yanban;Username=yanban;Password=yanban_dev_password";

        var options = new DbContextOptionsBuilder<YanbanDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new YanbanDbContext(options);
    }
}
