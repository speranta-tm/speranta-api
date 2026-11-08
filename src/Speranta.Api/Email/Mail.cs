using System.Net;
using MailKit.Net.Proxy;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Api.Push;
using Speranta.Infrastructure;

namespace Speranta.Api.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host. Gmail by default.</summary>
    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    /// <summary>SMTP username; for Gmail, the full address. Empty means <see cref="NotifyTo"/>.</summary>
    public string User { get; set; } = "";

    /// <summary>
    /// For Gmail, an APP password (16 letters, myaccount.google.com/apppasswords), not
    /// the account password. Unlike WP Mail SMTP's Google sign-in, it does not expire;
    /// it stops working only if the account password changes or 2-Step Verification
    /// is turned off.
    /// </summary>
    public string Password { get; set; } = "";

    /// <summary>The From address. Defaults to <see cref="User"/>.</summary>
    public string From { get; set; } = "";

    public string FromName { get; set; } = "Biserica Speranța Timișoara";

    /// <summary>Where new orders, sign-ups and messages are announced.</summary>
    public string NotifyTo { get; set; } = "speranta.azs.tm@gmail.com";

    /// <summary>The web client's base URL, for links in emails (admin pages, invitations).</summary>
    public string WebUrl { get; set; } = "http://localhost:5173";

    /// <summary>
    /// How often the API signs in to the mailbox without sending anything, so a dead
    /// password shows up in the admin even on a day with no orders.
    /// </summary>
    public double CheckIntervalHours { get; set; } = 6;

}

/// <summary>An email ready to go: what the transport needs.</summary>
public sealed record OutgoingMail(
    string To, string Subject, string HtmlBody,
    (SubmissionKind Kind, int Id)? RecordOn = null);

/// <summary>Puts mail in the database outbox. Never blocks a request on SMTP.</summary>
public interface IMailOutbox
{
    Task EnqueueAsync(OutgoingMail mail, CancellationToken ct);
}

public sealed class DbMailOutbox(SperantaDbContext db, TimeProvider clock, MailKick kick) : IMailOutbox
{
    public async Task EnqueueAsync(OutgoingMail mail, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        db.Outbox.Add(new OutboxEmail
        {
            To = mail.To,
            Subject = mail.Subject,
            HtmlBody = mail.HtmlBody,
            Kind = mail.RecordOn?.Kind,
            SubmissionId = mail.RecordOn?.Id,
            CreatedAt = now,
            NextAttemptAt = now,
        });
        await db.SaveChangesAsync(ct);
        kick.Signal();
    }
}

/// <summary>Wakes the sender as soon as something is queued, instead of at its next tick.</summary>
public sealed class MailKick
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Signal()
    {
        if (_signal.CurrentCount == 0)
        {
            _signal.Release();
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

public interface IMailTransport
{
    bool IsConfigured { get; }

    /// <summary>Sends, or throws with the server's reason.</summary>
    Task SendAsync(OutgoingMail mail, CancellationToken ct);

    /// <summary>Connects and signs in without sending anything; throws with the reason if it can't.</summary>
    Task CheckAsync(CancellationToken ct);
}

public sealed class SmtpMailTransport(IOptions<EmailOptions> options, MailCredentials credentials) : IMailTransport
{
    private readonly EmailOptions _options = options.Value;

    public bool IsConfigured => credentials.IsConfigured;

    public async Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _options.FromName,
            string.IsNullOrWhiteSpace(_options.From) ? credentials.User : _options.From));
        message.To.Add(MailboxAddress.Parse(mail.To));
        message.Subject = mail.Subject;
        message.Body = new BodyBuilder { HtmlBody = mail.HtmlBody }.ToMessageBody();

        using var client = await ConnectAsync(ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    public async Task CheckAsync(CancellationToken ct)
    {
        using var client = await ConnectAsync(ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    private async Task<SmtpClient> ConnectAsync(CancellationToken ct)
    {
        var client = new SmtpClient { Timeout = 30_000, ProxyClient = SmtpProxy.For(credentials.Host, credentials.Port) };
        try
        {
            var socket = credentials.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(credentials.Host, credentials.Port, socket, ct);
            await client.AuthenticateAsync(credentials.User, credentials.Password, ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}

/// <summary>
/// The HTTP proxy SMTP goes through, from the same <c>HTTPS_PROXY</c> / <c>NO_PROXY</c>
/// environment that <see cref="HttpClient"/> (and so Web Push) already follows.
///
/// On the Pi the API may not open connections to the internet itself: everything goes
/// through an egress proxy that allows only the hosts it needs (deploy/pi/README.md).
/// HttpClient picks the proxy up on its own; MailKit doesn't, so it is handed over here as
/// an HTTP CONNECT tunnel. With no proxy configured (development, tests) SMTP connects
/// directly, as before.
/// </summary>
public static class SmtpProxy
{
    public static IProxyClient? For(string host, int port, IWebProxy? proxies = null)
    {
        proxies ??= HttpClient.DefaultProxy;
        var target = new Uri($"https://{host}:{port}/");

        if (proxies.IsBypassed(target))
        {
            return null;
        }

        var proxy = proxies.GetProxy(target);
        return proxy is null || proxy == target ? null : new HttpProxyClient(proxy.Host, proxy.Port);
    }
}

/// <summary>
/// Delivers what is due from the outbox. Separate from the background loop so the
/// tests (and the admin's "send a test email") can run one pass on demand.
/// </summary>
public sealed class MailDelivery(
    SperantaDbContext db, IMailTransport transport, IPushNotifier push, TimeProvider clock, ILogger<MailDelivery> logger)
{
    /// <summary>
    /// Tells admins' phones that email stopped: once when it breaks, then at most daily
    /// while it stays broken. Push does not go through Gmail, so it still arrives.
    /// </summary>
    private void AlertFailing(MailStatus status)
    {
        var now = clock.GetUtcNow();
        if (status.LastAlertAt is { } last && now - last < TimeSpan.FromHours(24))
        {
            return;
        }

        status.LastAlertAt = now;
        push.Notify(new PushMessage(
            "Emailurile de pe site nu mai pleacă",
            "Comenzile și înscrierile sunt în panou. Intră în admin și apasă „Cum rezolv?”.",
            "/admin"));
    }

    /// <summary>Retry spacing: quick at first, then hourly, forever, until it goes through.</summary>
    internal static TimeSpan Backoff(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromHours(1),
    };

    /// <summary>Sends everything due. Returns how many went out.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        if (!transport.IsConfigured)
        {
            // Nothing to try with; the mail simply waits. The admin banner says why.
            return 0;
        }

        var due = await db.Outbox
            .Where(m => m.SentAt == null && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(25)
            .ToListAsync(ct);

        var status = await db.MailStatus.FirstAsync(ct);
        var sent = 0;

        foreach (var mail in due)
        {
            mail.Attempts++;
            try
            {
                await transport.SendAsync(new OutgoingMail(mail.To, mail.Subject, mail.HtmlBody), ct);
                mail.SentAt = clock.GetUtcNow();
                mail.LastError = null;
                status.LastSuccessAt = mail.SentAt;
                status.LastAlertAt = null; // working again: the next breakage alerts at once
                sent++;

                if (mail is { Kind: { } kind, SubmissionId: { } id })
                {
                    db.Activity.Add(new ActivityEntry
                    {
                        Kind = kind,
                        SubmissionId = id,
                        Text = $"Email trimis la {mail.To}",
                        At = mail.SentAt.Value,
                    });
                }

                logger.LogInformation("Sent '{Subject}' to {To}.", mail.Subject, mail.To);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                mail.LastError = Truncate(ex.Message);
                mail.NextAttemptAt = clock.GetUtcNow() + Backoff(mail.Attempts);
                status.LastFailureAt = clock.GetUtcNow();
                status.LastError = mail.LastError;
                AlertFailing(status);
                logger.LogError(ex, "Sending '{Subject}' to {To} failed (attempt {Attempt}).",
                    mail.Subject, mail.To, mail.Attempts);

                // Same mailbox, same credentials: if one failed the rest will too.
                // Push them all back together instead of hammering Gmail.
                foreach (var rest in due.Where(m => m.SentAt == null && m.Id != mail.Id))
                {
                    rest.NextAttemptAt = mail.NextAttemptAt;
                }

                break;
            }
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>Signs in to the mailbox and records whether that worked.</summary>
    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        var status = await db.MailStatus.FirstAsync(ct);
        status.LastCheckAt = clock.GetUtcNow();

        if (!transport.IsConfigured)
        {
            status.LastCheckOk = false;
            status.LastError = "SMTP nu este configurat.";
        }
        else
        {
            try
            {
                await transport.CheckAsync(ct);
                status.LastCheckOk = true;
                status.LastAlertAt = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                status.LastCheckOk = false;
                status.LastFailureAt = status.LastCheckAt;
                status.LastError = Truncate(ex.Message);
                AlertFailing(status);
                logger.LogError(ex, "Mailbox sign-in check failed.");
            }
        }

        await db.SaveChangesAsync(ct);
        return status.LastCheckOk == true;
    }

    private static string Truncate(string s) => s.Length > 1000 ? s[..1000] : s;
}

/// <summary>Runs <see cref="MailDelivery"/> whenever mail is queued, every 30 s regardless, and the sign-in check every few hours.</summary>
public sealed class MailSenderService(
    IServiceScopeFactory scopes, MailKick kick, IOptions<EmailOptions> options, TimeProvider clock,
    ILogger<MailSenderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(0.1, options.Value.CheckIntervalHours));
        var nextCheck = clock.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var delivery = scope.ServiceProvider.GetRequiredService<MailDelivery>();

                if (clock.GetUtcNow() >= nextCheck)
                {
                    await delivery.CheckAsync(stoppingToken);
                    nextCheck = clock.GetUtcNow() + interval;
                }

                await delivery.ProcessDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A database hiccup must not kill the sender for good.
                logger.LogError(ex, "Mail sender pass failed.");
            }

            try
            {
                await kick.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
