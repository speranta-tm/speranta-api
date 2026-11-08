using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Speranta.Api.Contracts;
using Speranta.Api.Email;
using Speranta.Api.Setup;
using Speranta.Domain.Entities;
using Speranta.Infrastructure;
using Speranta.Infrastructure.Identity;

namespace Speranta.Api.Endpoints;

/// <summary>Creates and redeems admin invitations.</summary>
public sealed class AdminInvites(
    SperantaDbContext db, Notifications notify, TimeProvider clock, IOptions<EmailOptions> email)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <summary>Stores the invitation and queues its email. Returns the link (for the bootstrap log).</summary>
    public async Task<string> CreateAsync(string address, string? displayName, AppUser? invitedBy, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.GetUtcNow();

        db.AdminInvites.Add(new AdminInvite
        {
            Email = address,
            TokenHash = Hash(token),
            DisplayName = displayName,
            InvitedByUserId = invitedBy?.Id,
            CreatedAt = now,
            ExpiresAt = now + Lifetime,
        });
        await db.SaveChangesAsync(ct);

        await notify.AdminInviteAsync(address, token, invitedBy?.DisplayName ?? invitedBy?.Email, ct);
        return $"{email.Value.WebUrl.TrimEnd('/')}/admin/invitatie?token={token}";
    }

    public async Task<AdminInvite?> FindOpenAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        var invite = await db.AdminInvites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        return invite is not null && invite.IsOpen(clock.GetUtcNow()) ? invite : null;
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class AdminPeopleEndpoints
{
    /// <summary>The signed-in admin's own profile.</summary>
    public static RouteGroupBuilder MapAccount(this RouteGroupBuilder admin)
    {
        admin.MapGet("/me", async (UserManager<AppUser> users, ClaimsPrincipal principal) =>
            await users.GetUserAsync(principal) is { } u
                ? Results.Ok(new AccountInfo(u.Id, u.Email!, u.DisplayName))
                : Results.NotFound());

        admin.MapPatch("/me", async (
            [FromBody] UpdateAccountRequest req, UserManager<AppUser> users, ClaimsPrincipal principal) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null)
            {
                return Results.NotFound();
            }

            var v = new Validation();
            user.DisplayName = v.Text("displayName", req.DisplayName, 100, false, "");
            if (!v.IsValid)
            {
                return v.Problem();
            }

            await users.UpdateAsync(user);
            return Results.Ok(new AccountInfo(user.Id, user.Email!, user.DisplayName));
        });

        return admin;
    }

    public static RouteGroupBuilder MapAdmins(this RouteGroupBuilder admin)
    {
        admin.MapGet("/admins", async (
            SperantaDbContext db, UserManager<AppUser> users, ClaimsPrincipal principal, TimeProvider clock,
            CancellationToken ct) => Results.Ok(await List(db, users, principal.Id(), clock, ct)));

        admin.MapPost("/admins/invites", async (
            [FromBody] CreateInviteRequest req, AdminInvites invites, SperantaDbContext db,
            UserManager<AppUser> users, ClaimsPrincipal principal, TimeProvider clock, CancellationToken ct) =>
        {
            var v = new Validation();
            var address = v.Email("email", req.Email, required: true);
            var displayName = v.Text("displayName", req.DisplayName, 100, false, "");
            if (!v.IsValid)
            {
                return v.Problem();
            }

            var existing = await users.FindByEmailAsync(address!);
            if (existing is not null && await users.IsInRoleAsync(existing, AppRoles.Admin))
            {
                v.Add("email", "Această persoană este deja administrator.");
                return v.Problem();
            }

            // A second invite to the same address replaces the first, so only the
            // newest emailed link works.
            var now = clock.GetUtcNow();
            var upper = address!.ToUpperInvariant();
            await db.AdminInvites
                .Where(i => i.Email.ToUpper() == upper && i.AcceptedAt == null && i.ExpiresAt > now)
                .ExecuteUpdateAsync(i => i.SetProperty(x => x.ExpiresAt, now), ct);

            await invites.CreateAsync(address, displayName, await users.GetUserAsync(principal), ct);
            return Results.Ok(await List(db, users, principal.Id(), clock, ct));
        })
            .RequireRateLimiting(RateLimitSetup.AuthPolicy)
            .WithSummary("Invite someone by email. The link is valid 7 days.");

        admin.MapDelete("/admins/invites/{id:int}", async (
            int id, SperantaDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var revoked = await db.AdminInvites.Where(i => i.Id == id && i.AcceptedAt == null)
                .ExecuteUpdateAsync(i => i.SetProperty(x => x.ExpiresAt, now), ct);
            return revoked == 0 ? Results.NotFound() : Results.NoContent();
        })
            .WithSummary("Withdraw an invitation that has not been accepted.");

        admin.MapDelete("/admins/{id:guid}", async (
            Guid id, UserManager<AppUser> users, ClaimsPrincipal principal) =>
        {
            if (id == principal.Id())
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["id"] = ["Nu vă puteți scoate singur din administratori."],
                });
            }

            var user = await users.FindByIdAsync(id.ToString());
            if (user is null || !await users.IsInRoleAsync(user, AppRoles.Admin))
            {
                return Results.NotFound();
            }

            // Removing the role is not enough on its own: the bearer token they
            // already hold carries it. Deleting the account ends that token at its
            // next refresh, and nobody here has a non-admin account to keep.
            await users.DeleteAsync(user);
            return Results.NoContent();
        })
            .WithSummary("Remove an administrator (not yourself).");

        return admin;
    }

    /// <summary>Anonymous: opening the emailed link, and choosing a password.</summary>
    public static IEndpointRouteBuilder MapInviteAcceptance(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/invites").RequireRateLimiting(RateLimitSetup.AuthPolicy);

        group.MapGet("/{token}", async (
            string token, AdminInvites invites, SperantaDbContext db, CancellationToken ct) =>
        {
            var invite = await invites.FindOpenAsync(token, ct);
            if (invite is null)
            {
                return Results.NotFound();
            }

            var by = invite.InvitedByUserId is { } uid
                ? await db.Users.Where(u => u.Id == uid).Select(u => u.DisplayName ?? u.Email).FirstOrDefaultAsync(ct)
                : null;

            return Results.Ok(new InviteInfo(invite.Email, invite.DisplayName, by));
        })
            .WithSummary("Who the invitation is for. 404 when it is unknown, used or expired.");

        group.MapPost("/{token}/accept", async (
            string token, [FromBody] AcceptInviteRequest req, AdminInvites invites, SperantaDbContext db,
            UserManager<AppUser> users, TimeProvider clock, CancellationToken ct) =>
        {
            var invite = await invites.FindOpenAsync(token, ct);
            if (invite is null)
            {
                return Results.NotFound();
            }

            var v = new Validation();
            var name = v.Text("displayName", req.DisplayName, 100, false, "") ?? invite.DisplayName;
            if (string.IsNullOrEmpty(req.Password))
            {
                v.Add("password", "Alegeți o parolă.");
            }

            if (!v.IsValid)
            {
                return v.Problem();
            }

            var now = clock.GetUtcNow();
            var user = await users.FindByEmailAsync(invite.Email);
            IdentityResult result;

            if (user is null)
            {
                user = new AppUser
                {
                    UserName = invite.Email,
                    Email = invite.Email,
                    EmailConfirmed = true,
                    DisplayName = name,
                    CreatedAt = now,
                };
                result = await users.CreateAsync(user, req.Password!);
            }
            else
            {
                // An account without the role (removed earlier, or made by hand): the
                // invite proves control of the inbox, so it may set a new password.
                user.DisplayName = name ?? user.DisplayName;
                var resetToken = await users.GeneratePasswordResetTokenAsync(user);
                result = await users.ResetPasswordAsync(user, resetToken, req.Password!);
            }

            if (!result.Succeeded)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = [.. result.Errors.Select(e => PasswordMessage(e.Code, e.Description))],
                });
            }

            await users.AddToRoleAsync(user, AppRoles.Admin);
            invite.AcceptedAt = now;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AccountInfo(user.Id, user.Email!, user.DisplayName));
        })
            .WithSummary("Accept an invitation: creates the admin account. Sign in with /auth/login afterwards.");

        return app;
    }

    private static string PasswordMessage(string code, string fallback) => code switch
    {
        "PasswordTooShort" => "Parola trebuie să aibă cel puțin 10 caractere.",
        "PasswordRequiresUniqueChars" => "Parola are prea puține caractere diferite.",
        _ => fallback,
    };

    private static async Task<AdminsDto> List(
        SperantaDbContext db, UserManager<AppUser> users, Guid me, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var admins = (await users.GetUsersInRoleAsync(AppRoles.Admin))
            .OrderBy(u => u.DisplayName ?? u.Email)
            .Select(u => new AdminUserDto(u.Id, u.Email!, u.DisplayName, u.Id == me))
            .ToList();
        var invites = await db.AdminInvites.AsNoTracking()
            .Where(i => i.AcceptedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new PendingInviteDto(i.Id, i.Email, i.DisplayName, i.CreatedAt, i.ExpiresAt))
            .ToListAsync(ct);

        return new AdminsDto(admins, invites);
    }
}
