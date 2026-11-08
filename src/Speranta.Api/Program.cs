using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Speranta.Api.Email;
using Speranta.Api.Endpoints;
using Speranta.Api.Setup;
using Speranta.Api.Storage;
using Speranta.Infrastructure;
using Speranta.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSperantaPersistence(builder.Configuration);
builder.Services.AddSperantaDataProtection(builder.Configuration);
builder.Services.AddSperantaIdentity();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<SperantaDbContext>();

// Enums travel as names ("Confirmed"), both ways. Numbers are still accepted.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Injected rather than read inline, so tests control "now".
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddReverseProxySupport();
builder.Services.AddSperantaRateLimiting(builder.Configuration);
builder.Services.AddSperantaCors(builder.Configuration);

builder.Services.AddOptions<EmailOptions>().Bind(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddOptions<StorageOptions>().Bind(builder.Configuration.GetSection(StorageOptions.SectionName));

// Mail is queued in the database and sent by a background service, so a visitor
// never waits on Gmail, and a refused or broken mailbox delays notifications
// instead of losing them: they are retried until they go through.
builder.Services.AddSingleton<MailKick>();
builder.Services.AddScoped<IMailOutbox, DbMailOutbox>();
builder.Services.AddSingleton<IMailTransport, SmtpMailTransport>();
builder.Services.AddScoped<MailDelivery>();
builder.Services.AddScoped<MailHealth>();
builder.Services.AddHostedService<MailSenderService>();
builder.Services.AddScoped<Notifications>();

// Transient, not scoped: MapIdentityApi resolves it once from the ROOT provider,
// which scope validation forbids for a scoped service in Development.
builder.Services.AddTransient<IEmailSender<AppUser>, IdentityEmailSender>();

builder.Services.AddScoped<FileStorage>();
builder.Services.AddScoped<AdminInvites>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Before the rate limiter, so per-IP partitions see the real client address.
app.UseForwardedHeaders();
app.UseForwardedPrefix();
app.UseCors(CorsSetup.PolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// /login, /refresh, /forgotPassword, /resetPassword, /manage/info. There is no
// public sign-up: /register answers 404 and admins arrive only by invitation.
// Middleware rather than an endpoint filter, because body binding runs before a
// filter and a malformed body would otherwise get a 400 that reveals the route.
app.Use((context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/auth/register", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    return next(context);
});

var auth = app.MapGroup("/auth").RequireRateLimiting(RateLimitSetup.AuthPolicy);
auth.MapIdentityApi<AppUser>();

app.MapPublicContent();
app.MapForms();
app.MapInviteAcceptance();

var admin = app.MapGroup("/admin").RequireAuthorization(IdentitySetup.AdminPolicy);

// A bearer token outlives the account it was issued for (up to an hour). Checking
// the account still exists makes removing an administrator take effect at once.
admin.AddEndpointFilter(async (context, next) =>
{
    var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
    return await users.GetUserAsync(context.HttpContext.User) is null
        ? Results.Unauthorized()
        : await next(context);
});

admin.MapAccount();
admin.MapAdminSubmissions();
admin.MapAdminContent();
admin.MapAdmins();
admin.MapAdminMail();

app.MapGet("/", () => Results.Ok(new
{
    service = "speranta-api",
    status = "ok",
    docs = "/openapi/v1.json",
    health = "/health",
}))
    .AllowAnonymous()
    .DisableRateLimiting();

// Round-trips the database, so green means the API can actually serve.
app.MapHealthChecks("/health").DisableRateLimiting();

await StartupTasks.RunAsync(app.Services, app.Configuration, app.Logger);

app.Run();

/// <summary>Marker so the integration tests can host this app in-process.</summary>
public partial class Program;
