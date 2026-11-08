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

        return admin;
    }
}
