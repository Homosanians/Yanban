using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Xunit;

namespace Yanban.IntegrationTests;

/// <summary>
/// Boots the real API via WebApplicationFactory against throwaway Postgres and MinIO
/// containers. Config is supplied through environment variables (read by
/// WebApplication.CreateBuilder at startup) so it is in place before Program reads it.
/// The app applies migrations and ensures the bucket on startup. Because both the
/// in-process API and the test's plain HttpClient reach MinIO at the same mapped
/// localhost port, presigned URLs work end-to-end here.
/// </summary>
public class YanbanApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly MinioContainer _minio = new MinioBuilder()
        .WithImage("minio/minio:RELEASE.2023-01-31T02-24-19Z")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _minio.StartAsync());

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Secret", "integration-test-signing-key-at-least-32-bytes-long");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "yanban-test");
        Environment.SetEnvironmentVariable("Jwt__Audience", "yanban-test");
        Environment.SetEnvironmentVariable("Jwt__AccessTokenMinutes", "15");
        Environment.SetEnvironmentVariable("Jwt__RefreshTokenDays", "30");

        Environment.SetEnvironmentVariable("S3__Endpoint", _minio.GetConnectionString());
        Environment.SetEnvironmentVariable("S3__AccessKey", _minio.GetAccessKey());
        Environment.SetEnvironmentVariable("S3__SecretKey", _minio.GetSecretKey());
        Environment.SetEnvironmentVariable("S3__Bucket", "yanban-attachments-test");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _minio.DisposeAsync();
        await base.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<YanbanApiFactory> { }
