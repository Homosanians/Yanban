using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Infrastructure.Activities;
using Yanban.Infrastructure.Attachments;
using Yanban.Infrastructure.Auth;
using Yanban.Infrastructure.Boards;
using Yanban.Infrastructure.Caching;
using Yanban.Infrastructure.Cards;
using Yanban.Infrastructure.Comments;
using Yanban.Infrastructure.Lists;
using Yanban.Infrastructure.Persistence;
using Yanban.Infrastructure.Security;
using Yanban.Infrastructure.Storage;

namespace Yanban.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<YanbanDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres"))
                   .UseSnakeCaseNamingConvention());

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IBoardService, BoardService>();
        services.AddScoped<IListService, ListService>();
        services.AddScoped<ICardService, CardService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IActivityService, ActivityService>();
        // Scoped so it shares the request DbContext with the services it audits — that
        // shared unit of work is what makes each audit row commit with its mutation.
        services.AddScoped<IActivityRecorder, ActivityRecorder>();
        // The same table read as an outbox, by the realtime tailer (ADR-11).
        services.AddScoped<IActivityOutbox, ActivityOutbox>();

        // Object storage (S3-compatible; MinIO in dev). One SDK client pointed at the
        // configured endpoint with path-style addressing (required by MinIO). NOTE: the
        // same endpoint is baked into presigned URLs — see ADR-10 for the split-horizon
        // limitation when the API and the browser reach storage at different hosts.
        services.Configure<S3Options>(configuration.GetSection(S3Options.SectionName));
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<S3Options>>().Value;
            var config = new AmazonS3Config
            {
                ServiceURL = o.Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1"
            };
            return new AmazonS3Client(new BasicAWSCredentials(o.AccessKey, o.SecretKey), config);
        });
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddScoped<IAttachmentService, AttachmentService>();

        return services;
    }
}
