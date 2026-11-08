using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Speranta.Api.Endpoints;
using Speranta.Infrastructure;
using Speranta.Infrastructure.Identity;

namespace Speranta.Api.Setup;

public static class StartupTasks
{
    /// <summary>
    /// Creates the roles, then makes sure every address in Bootstrap:AdminEmails can
    /// get in. There is no public sign-up, so this is how the first admin appears:
    /// an existing account is promoted; an address with no account gets an
    /// invitation, emailed and also written to the log (so a first install works
    /// before SMTP is set up). No default password ships anywhere.
    /// </summary>
    public static async Task RunAsync(
        IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var roles = sp.GetRequiredService<RoleManager<AppRole>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new AppRole { Name = role });
            }
        }

        var adminEmails = configuration.GetSection("Bootstrap:AdminEmails").Get<string[]>() ?? [];
        if (adminEmails.Length == 0)
        {
            return;
        }

        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var db = sp.GetRequiredService<SperantaDbContext>();
        var invites = sp.GetRequiredService<AdminInvites>();
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();

        foreach (var raw in adminEmails)
        {
            var email = raw.Trim();
            var user = await users.FindByEmailAsync(email);

            if (user is not null)
            {
                if (!await users.IsInRoleAsync(user, AppRoles.Admin))
                {
                    await users.AddToRoleAsync(user, AppRoles.Admin);
                    logger.LogInformation("Granted Admin to {Email}.", email);
                }

                continue;
            }

            var normalized = email.ToUpperInvariant();
            var hasOpenInvite = await db.AdminInvites.AnyAsync(i =>
                i.Email.ToUpper() == normalized && i.AcceptedAt == null && i.ExpiresAt > now);

            if (hasOpenInvite)
            {
                continue;
            }

            var link = await invites.CreateAsync(email, invitedBy: null, CancellationToken.None);
            logger.LogWarning(
                "Bootstrap invitation for {Email}: {Link} (valid 7 days, also emailed).", email, link);
        }
    }
}
