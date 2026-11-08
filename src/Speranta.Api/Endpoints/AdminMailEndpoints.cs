using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Speranta.Api.Email;
using Speranta.Infrastructure;

namespace Speranta.Api.Endpoints;

/// <summary>
/// The admin's view of email delivery. The old WordPress site stopped sending every
/// few days without telling anyone; here the admin sees it on every page, and can
/// check and resend after fixing the password.
/// </summary>
public static class AdminMailEndpoints
{
    public static RouteGroupBuilder MapAdminMail(this RouteGroupBuilder admin)
    {
        admin.MapGet("/mail-status", async (MailHealth health, CancellationToken ct) =>
            Results.Ok(await health.GetAsync(ct)))
            .WithSummary("Is email getting out? Drives the warning banner shown on every admin page.");

        // "Verifică acum": sign in to the mailbox, then try everything waiting
        // straight away instead of at its next scheduled retry.
        admin.MapPost("/mail-status/check", async (
            MailDelivery delivery, MailHealth health, SperantaDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (await delivery.CheckAsync(ct))
            {
                var now = clock.GetUtcNow();
                await db.Outbox.Where(m => m.SentAt == null)
                    .ExecuteUpdateAsync(m => m.SetProperty(x => x.NextAttemptAt, now), ct);
                await delivery.ProcessDueAsync(ct);
            }

            return Results.Ok(await health.GetAsync(ct));
        })
            .WithSummary("Check the mailbox now and, if it works, send everything that is waiting.");

        admin.MapPost("/mail-status/test", async (
            IMailOutbox outbox, MailDelivery delivery, MailHealth health, IOptions<EmailOptions> options,
            TimeProvider clock, CancellationToken ct) =>
        {
            var to = options.Value.NotifyTo;
            await outbox.EnqueueAsync(new OutgoingMail(to, "Test: emailurile de pe site funcționează",
                "<div style=\"font-family:Inter,Arial,sans-serif;font-size:15px;color:#1A202C\">"
                + "<p>Acesta este un email de test trimis din panoul de administrare al site-ului.</p>"
                + "<p>Dacă îl vezi, comenzile, înscrierile și mesajele de pe site ajung aici.</p>"
                + $"<p style=\"color:#718096;font-size:13px\">Trimis la {clock.GetUtcNow():yyyy-MM-dd HH:mm} UTC</p></div>"), ct);
            await delivery.ProcessDueAsync(ct);

            return Results.Ok(await health.GetAsync(ct));
        })
            .WithSummary("Send a test email to the notification address now.");

        // "Cum rezolv?": the admin pastes a new Gmail app password. It is stored
        // encrypted, used at once, checked, and everything waiting goes out.
        admin.MapPut("/mail-settings", async (
            [FromBody] MailSettingsRequest req, SperantaDbContext db, MailCredentials credentials,
            IDataProtectionProvider protection, MailDelivery delivery, MailHealth health, TimeProvider clock,
            CancellationToken ct) =>
        {
            // Google shows it as four groups of four letters; the spaces are not part of it.
            var password = new string((req.AppPassword ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (password.Length is < 8 or > 100)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["appPassword"] = ["Lipește parola de aplicație: 16 litere, așa cum o arată Google."],
                });
            }

            var settings = await db.Settings.FirstAsync(ct);
            settings.SmtpPasswordProtected = protection.CreateProtector(MailCredentials.ProtectorPurpose).Protect(password);
            await db.SaveChangesAsync(ct);
            credentials.UseSaved(password);

            if (await delivery.CheckAsync(ct))
            {
                var now = clock.GetUtcNow();
                await db.Outbox.Where(m => m.SentAt == null)
                    .ExecuteUpdateAsync(m => m.SetProperty(x => x.NextAttemptAt, now), ct);
                await delivery.ProcessDueAsync(ct);
            }

            return Results.Ok(await health.GetAsync(ct));
        })
            .WithSummary("Save a new Gmail app password (encrypted), check it, and send what is waiting.");

        return admin;
    }
}

public sealed record MailSettingsRequest(string? AppPassword);
