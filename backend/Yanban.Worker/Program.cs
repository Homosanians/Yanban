using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Infrastructure.Notifications;
using Yanban.Infrastructure.Persistence;
using Yanban.Worker;

var builder = Host.CreateApplicationBuilder(args);

// --- a narrow DI root, on purpose ------------------------------------------------------------
//
// NOT `AddInfrastructure()`. That registers the whole API's world — including IActivityRecorder,
// which depends on ICurrentUser, which reads HttpContext and is only ever registered by the API.
// A headless worker would fail DI validation on a dependency it has no use for. It also drags in
// the S3 client stack and JwtOptions to send an email.
//
// So: the DbContext, the options this host actually reads, and the two services that do the work.
builder.Services.AddDbContext<YanbanDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"))
           .UseSnakeCaseNamingConvention());

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<OutboxProcessor>();

builder.Services.AddHostedService<OutboxWorker>();

// Note what is *absent*: Database.Migrate(). The API owns the schema. Two containers racing to
// apply the same migration on startup is a bug looking for a slow morning.

var host = builder.Build();
await host.RunAsync();
