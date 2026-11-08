using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Speranta.Api.Setup;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Everything, per client IP.</summary>
    public int GlobalPermitsPerMinute { get; set; } = 600;

    /// <summary>Sign-in, password reset and invitations, per client IP.</summary>
    public int AuthPermitsPerMinute { get; set; } = 20;

    /// <summary>
    /// The public forms, per client IP. A family ordering two books and signing up a
    /// child fits easily; a script filling the church's inbox does not. The forms have
    /// no CAPTCHA (the old site had none either), so this and the honeypot are the guard.
    /// </summary>
    public int FormPermitsPerTenMinutes { get; set; } = 10;
}

public static class RateLimitSetup
{
    public const string AuthPolicy = "auth";
    public const string FormPolicy = "forms";

    public static IServiceCollection AddSperantaRateLimiting(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitOptions.SectionName)
            .Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.GlobalPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                    }));

            limiter.AddPolicy(AuthPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.AuthPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                    }));

            limiter.AddPolicy(FormPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.FormPermitsPerTenMinutes,
                        Window = TimeSpan.FromMinutes(10),
                    }));
        });

        return services;
    }

    // In-process test requests have no remote IP: one shared bucket is the safe
    // reading of that, not no limit at all.
    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
