using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Infrastructure.Notifications;
using Yanban.Infrastructure.Persistence;
using Yanban.Infrastructure.Storage;
using Yanban.Worker;

var builder = Host.CreateApplicationBuilder(args);

// --- a narrow DI root, on purpose ------------------------------------------------------------
//
// NOT `AddInfrastructure()`. That registers the whole API's world — including IActivityRecorder,
// which depends on ICurrentUser, which reads HttpContext and is only ever registered by the API.
// A headless worker would fail DI validation on a dependency it has no use for.
//
// So: the DbContext, the options this host reads, and the services that do the work. The worker
// *does* need object storage now (the janitor deletes from S3), but only the internal client —
// it never mints presigned URLs, so there is no split-horizon second client to build.
builder.Services.AddDbContext<YanbanDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
           .UseSnakeCaseNamingConvention());

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<OutboxProcessor>();

builder.Services.Configure<S3Options>(builder.Configuration.GetSection(S3Options.SectionName));
builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var o = sp.GetRequiredService<IOptions<S3Options>>().Value;
    return S3ClientFactory.Create(o.Endpoint, o);
});
// The presign client is keyed and never opens a socket; the worker never presigns, but
// S3ObjectStorage resolves the key in its constructor, so register it to the same instance.
builder.Services.AddKeyedSingleton<IAmazonS3>(
    S3ObjectStorage.PresignClientKey,
    (sp, _) => sp.GetRequiredService<IAmazonS3>());
builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();
builder.Services.AddScoped<StorageJanitor>();

builder.Services.AddHostedService<OutboxWorker>();

// Note what is *absent*: Database.Migrate(). The API owns the schema. Two containers racing to
// apply the same migration on startup is a bug looking for a slow morning.

var host = builder.Build();
await host.RunAsync();
