using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Amazon.Runtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using Yanban.API.Authorization;
using Yanban.API.Identity;
using Yanban.API.Middleware;
using Yanban.API.Realtime;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Infrastructure;
using Yanban.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    // Serialize/accept enums as their names (e.g. "Admin") rather than ints.
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// OpenAPI document + a Bearer security scheme so the Scalar docs UI can authorize.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste a JWT access token obtained from /auth/login."
        };
        document.SecurityRequirements.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            }] = Array.Empty<string>()
        });
        return Task.CompletedTask;
    });
});

builder.Services.AddInfrastructure(builder.Configuration);

// Realtime (M7). The hub only gates who may listen; the hosted tailer does the sending,
// reading the ActivityLog written by M5 as an outbox. No SignalR backplane on purpose —
// see ADR-11.
builder.Services.Configure<RealtimeOptions>(builder.Configuration.GetSection(RealtimeOptions.SectionName));
builder.Services.AddSignalR();
builder.Services.AddSingleton<BoardSubscriptionRegistry>();
builder.Services.AddHostedService<ActivityDispatcher>();

// Ambient caller identity for audit trails. The accessor exposes the request's
// principal; ICurrentUser reads its sub claim so Infrastructure can record "who did
// it" without taking a dependency on HttpContext.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
          ?? throw new InvalidOperationException("Missing Jwt configuration section.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep original JWT claim names (so sub stays sub, not the SOAP URI).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            // No grace period: a 15-min token expires at 15 min, not 20.
            // Safe here because the same backend issues and validates (one clock).
            ClockSkew = TimeSpan.Zero
        };

        // Identity is stateless (the signed JWT), but token validity is always
        // checked against current state: the tv claim must match the current
        // TokenVersion (cached ~60s, invalidated on logout-all).
        options.Events = new JwtBearerEvents
        {
            // A browser cannot set an Authorization header on a WebSocket handshake, so the
            // SignalR client puts the token in the query string instead. Honor it only under
            // /hubs: tokens in URLs leak into access logs, proxies and referrers, and the
            // REST API has no reason to accept one.
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = token;

                return Task.CompletedTask;
            },

            OnTokenValidated = async context =>
            {
                var services = context.HttpContext.RequestServices;
                var cache = services.GetRequiredService<ICacheService>();
                var db = services.GetRequiredService<YanbanDbContext>();

                var sub = context.Principal?.FindFirst("sub")?.Value;
                var tvClaim = context.Principal?.FindFirst("tv")?.Value;
                if (!Guid.TryParse(sub, out var userId) || !int.TryParse(tvClaim, out var tokenVersion))
                {
                    context.Fail("Invalid token claims.");
                    return;
                }

                var current = await cache.GetOrCreateAsync(
                    $"tv:{userId}",
                    async () =>
                    {
                        var value = await db.Users
                            .Where(u => u.Id == userId)
                            .Select(u => (int?)u.TokenVersion)
                            .FirstOrDefaultAsync();
                        return value ?? -1; // -1 => user no longer exists
                    },
                    TimeSpan.FromSeconds(60));

                if (current < 0 || current != tokenVersion)
                {
                    context.Fail("Token has been revoked.");
                    return;
                }

                // Stamp the ticket with the token's own expiry. A REST call re-authenticates
                // on every request, but a WebSocket authenticates once at the handshake — so
                // without this, a live hub connection would outlive its token indefinitely
                // (verified: it kept receiving events straight through a logout-all). SignalR's
                // CloseOnAuthenticationExpiration reads this to hang up at expiry. JwtBearer
                // does not set it on its own.
                context.Properties.ExpiresUtc =
                    new DateTimeOffset(DateTime.SpecifyKind(context.SecurityToken.ValidTo, DateTimeKind.Utc));
            }
        };
    });

builder.Services.AddAuthorization();

// Resource-based authorization for boards (RBAC role × ABAC attributes). Scoped so
// the handler can resolve the caller's membership via the request DbContext.
builder.Services.AddScoped<IAuthorizationHandler, BoardAuthorizationHandler>();

// Throttle the credential-guessing surface: fixed window per client IP on /auth/*.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                // Relaxed under the Testing environment so integration suites are deterministic.
                PermitLimit = builder.Environment.IsEnvironment("Testing") ? 10_000 : 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Apply migrations on startup (dev convenience; Postgres must be reachable).
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    services.GetRequiredService<YanbanDbContext>().Database.Migrate();

    // Ensure the attachments bucket exists. Only attempted when storage is configured,
    // and tolerant of it being unreachable so the API still boots (attachments simply
    // stay unavailable until storage is up). A non-connectivity error still surfaces.
    var s3 = services.GetRequiredService<IOptions<S3Options>>().Value;
    if (!string.IsNullOrWhiteSpace(s3.Endpoint))
    {
        try
        {
            await services.GetRequiredService<IObjectStorage>().EnsureBucketAsync(CancellationToken.None);
        }
        // AmazonClientException is the base of AmazonServiceException and wraps the
        // connection-refused case (MinIO not running) — the realistic dev down-path —
        // while staying specific enough not to swallow a config error or NRE.
        catch (Exception ex) when (ex is HttpRequestException or AmazonClientException)
        {
            app.Logger.LogWarning(ex, "Object storage unreachable at startup; attachments are unavailable until it is up.");
        }
    }
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseRateLimiter();

// Interactive API reference (Scalar) over the OpenAPI document, in Development.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Yanban API"));
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<BoardHub>("/hubs/board", options =>
{
    // A hub is authorized once, at the handshake. Close the connection when the access
    // token expires so a socket cannot outlive the credential that opened it — this is
    // what bounds how long a revoked session (logout-all) can keep watching a board.
    options.CloseOnAuthenticationExpiration = true;
});

app.Run();

// Exposed so integration tests can spin up the app via WebApplicationFactory (M10).
public partial class Program;
