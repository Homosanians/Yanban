using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace Yanban.IntegrationTests;

/// <summary>
/// Boots the real API via WebApplicationFactory against a throwaway Postgres
/// container. Config is supplied through environment variables (read by
/// WebApplication.CreateBuilder at startup) so it is in place before Program
/// reads the Jwt section. The app applies migrations on startup.
/// </summary>
public class YanbanApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Secret", "integration-test-signing-key-at-least-32-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "yanban-test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "yanban-test");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", "30");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<YanbanApiFactory> { }
