using Microsoft.AspNetCore.DataProtection;
using Speranta.Infrastructure;
using Speranta.Infrastructure.Identity;

namespace Speranta.Api.Setup;

/// <summary>
/// Identity wiring lives in the API rather than Infrastructure: AddIdentityApiEndpoints
/// is part of the ASP.NET Core shared framework, and the persistence layer should not
/// depend on the web stack.
/// </summary>
public static class IdentitySetup
{
    public const string AdminPolicy = "admin";

    /// <summary>
    /// Persists Data Protection keys when a path is configured. The bearer tokens are
    /// protected with these keys; kept only in memory, every restart would sign every
    /// admin out.
    /// </summary>
    public static IServiceCollection AddSperantaDataProtection(
        this IServiceCollection services, IConfiguration configuration)
    {
        var keyPath = configuration["DataProtection:KeyPath"];
        var protection = services.AddDataProtection().SetApplicationName("speranta-api");

        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        }

        return services;
    }

    public static IServiceCollection AddSperantaIdentity(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, p => p.RequireRole(AppRoles.Admin));

        services
            .AddIdentityApiEndpoints<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = false;

                // Length carries far more real strength than symbol classes, which
                // mostly produce "Pa$$w0rd".
                options.Password.RequiredLength = 10;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireDigit = false;

                options.Lockout.MaxFailedAccessAttempts = 10;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<SperantaDbContext>();

        return services;
    }
}
