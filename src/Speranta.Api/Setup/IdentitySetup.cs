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

    /// <summary>The only password rule: at least this many characters, of any kind.</summary>
    public const int MinPasswordLength = 4;

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

                // A minimum length and nothing else: no character classes. Identity requires a
                // lowercase letter by default, so that one is switched off explicitly too.
                options.Password.RequiredLength = MinPasswordLength;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireDigit = false;

                options.Lockout.MaxFailedAccessAttempts = 10;
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<SperantaDbContext>();

        return services;
    }
}
