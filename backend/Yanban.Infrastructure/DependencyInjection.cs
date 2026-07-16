using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Boards;
using Yanban.Application.Common;
using Yanban.Infrastructure.Activities;
using Yanban.Infrastructure.Attachments;
using Yanban.Infrastructure.Auth;
using Yanban.Infrastructure.Boards;
using Yanban.Infrastructure.Caching;
using Yanban.Infrastructure.Cards;
using Yanban.Infrastructure.Comments;
using Yanban.Infrastructure.Lists;
using Yanban.Infrastructure.Notifications;
using Yanban.Infrastructure.Persistence;
using Yanban.Infrastructure.Search;
using Yanban.Infrastructure.Security;
using Yanban.Infrastructure.Storage;
using Yanban.Infrastructure.Templates;

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
        services.Configure<BoardTemplateOptions>(configuration.GetSection(BoardTemplateOptions.SectionName));
        services.AddScoped<IBoardService, BoardService>();
        services.AddScoped<IListService, ListService>();
        services.AddScoped<ICardService, CardService>();
        services.AddScoped<ICardSearchService, CardSearchService>();
        services.AddScoped<ICardTemplateService, CardTemplateService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IActivityService, ActivityService>();
        // Scoped so it shares the request DbContext with the services it audits; that shared
        // unit of work is what makes each audit row commit with its mutation.
        services.AddScoped<IActivityRecorder, ActivityRecorder>();
        // The same table read as an outbox, by the realtime tailer.
        services.AddScoped<IActivityOutbox, ActivityOutbox>();

        // Email notifications. Scoped for the same reason the recorder is: the outbox row has to
        // land in the mutation's own unit of work, or we would be promising to send mail about
        // changes that never committed.
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationOutbox, NotificationOutbox>();

        // Object storage (S3-compatible; MinIO in dev), path-style addressing (required by MinIO).
        //
        // Two clients, one job each. The API reaches storage on the internal network
        // (`minio:9000` under Compose), but a presigned URL handed to the browser must name a host
        // the browser can reach (`localhost:9000`), and the host is part of the SigV4 signature,
        // so it cannot be swapped in afterwards.
        //
        // A client can sign for a host it cannot itself reach: presigning is a local signature
        // computation, not a request. So the presign client never opens a socket, while every
        // operation that does (bucket creation, HEAD, DELETE) stays on the internal client.
        services.Configure<S3Options>(configuration.GetSection(S3Options.SectionName));
        services.AddSingleton<IAmazonS3>(sp => S3ClientFactory.Create(S3Config(sp).Endpoint, S3Config(sp)));
        services.AddKeyedSingleton<IAmazonS3>(S3ObjectStorage.PresignClientKey, (sp, _) =>
        {
            var o = S3Config(sp);
            // No public endpoint means the API and the browser reach storage the same way (the
            // "run the API on the host" case). Reuse the one client rather than build a twin.
            return string.IsNullOrWhiteSpace(o.PublicEndpoint)
                ? sp.GetRequiredService<IAmazonS3>()
                : S3ClientFactory.Create(o.PublicEndpoint, o);
        });
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();

        // The quota is the same for every board today, so the policy is a singleton over options.
        // The interface is what matters: a per-board or per-plan policy replaces this without
        // AttachmentService knowing.
        services.Configure<QuotaOptions>(configuration.GetSection(QuotaOptions.SectionName));
        services.AddSingleton<IBoardQuotaPolicy, ConfiguredBoardQuotaPolicy>();
        services.AddScoped<IAttachmentService, AttachmentService>();

        return services;
    }

    private static S3Options S3Config(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<S3Options>>().Value;
}
