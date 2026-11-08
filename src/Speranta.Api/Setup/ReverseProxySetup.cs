using Microsoft.AspNetCore.HttpOverrides;

namespace Speranta.Api.Setup;

public static class ReverseProxySetup
{
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Only nginx on this box forwards to us. Clearing the defaults and
            // trusting loopback alone stops a client spoofing X-Forwarded-For to
            // dodge the per-IP rate limits.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(System.Net.IPAddress.Loopback);
            options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
        });

        return services;
    }

    /// <summary>
    /// Honours <c>X-Forwarded-Prefix</c> as the path base.
    ///
    /// nginx serves this app under a sub-path and strips it before proxying, so
    /// without this the app builds every Location header and redirect without the
    /// prefix and clients get sent to a 404 one level up.
    ///
    /// This sets PathBase only; it does NOT strip a prefix still present on the
    /// path. The proxy must strip it (nginx does, via the trailing slash on
    /// proxy_pass). Rewriting Request.Path here would be useless anyway:
    /// WebApplication inserts UseRouting ahead of this middleware, so the endpoint
    /// has already been matched by the time it runs.
    /// </summary>
    public static IApplicationBuilder UseForwardedPrefix(this IApplicationBuilder app)
    {
        return app.Use((context, next) =>
        {
            var prefix = context.Request.Headers["X-Forwarded-Prefix"].FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(prefix))
            {
                context.Request.PathBase = new PathString(prefix.TrimEnd('/'));
            }

            return next();
        });
    }
}
