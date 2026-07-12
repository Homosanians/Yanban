using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Infrastructure.Activities;
using Yanban.Infrastructure.Auth;
using Yanban.Infrastructure.Boards;
using Yanban.Infrastructure.Caching;
using Yanban.Infrastructure.Cards;
using Yanban.Infrastructure.Comments;
using Yanban.Infrastructure.Lists;
using Yanban.Infrastructure.Persistence;
using Yanban.Infrastructure.Security;

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

        return services;
    }
}
