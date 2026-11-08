using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure;

namespace Speranta.Api.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host, e.g. smtp.gmail.com. Empty disables sending (it is logged instead).</summary>
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    /// <summary>SMTP username; for Gmail, the full address.</summary>
    public string User { get; set; } = "";

    /// <summary>For Gmail, an app password, not the account password.</summary>
    public string Password { get; set; } = "";

    /// <summary>The From address. Defaults to <see cref="User"/>.</summary>
    public string From { get; set; } = "";

    public string FromName { get; set; } = "Biserica Speranța Timișoara";

    /// <summary>Where new orders, sign-ups and messages are announced.</summary>
    public string NotifyTo { get; set; } = "speranta.azs.tm@gmail.com";

    /// <summary>The web client's base URL, for links in emails (admin pages, invitations).</summary>
    public string WebUrl { get; set; } = "http://localhost:5173";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(User)
        && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>
/// An email waiting to go out. <see cref="RecordOn"/>, when set, adds a history
/// line to that submission once the email has actually been sent, so the admin's
/// "Istoric" never claims a delivery that failed.
/// </summary>
public sealed record OutgoingMail(
    string To, string Subject, string HtmlBody,
    (SubmissionKind Kind, int Id)? RecordOn = null);

/// <summary>Hands mail to the background sender. Never blocks a request on SMTP.</summary>
public interface IMailOutbox
{
    void Enqueue(OutgoingMail mail);
}

public sealed class ChannelMailOutbox : IMailOutbox
{
    private readonly Channel<OutgoingMail> _channel =
        Channel.CreateBounded<OutgoingMail>(new BoundedChannelOptions(500)
        {
            // A flood of submissions must not grow memory without bound. Dropping
            // the oldest notification is acceptable: every submission is in the
            // database and on the admin list regardless.
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public ChannelReader<OutgoingMail> Reader => _channel.Reader;

    public void Enqueue(OutgoingMail mail) => _channel.Writer.TryWrite(mail);
}

public interface IMailTransport
{
    /// <summary>Returns false when nothing was sent (SMTP not configured).</summary>
    Task<bool> SendAsync(OutgoingMail mail, CancellationToken ct);
}

public sealed class SmtpMailTransport(
    IOptions<EmailOptions> options, ILogger<SmtpMailTransport> logger) : IMailTransport
{
    private readonly EmailOptions _options = options.Value;

    public async Task<bool> SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            logger.LogWarning(
                "Email to {To} ('{Subject}') not sent: SMTP is not configured. "
                + "Set Email:Host, Email:User and Email:Password.", mail.To, mail.Subject);
            return false;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _options.FromName,
            string.IsNullOrWhiteSpace(_options.From) ? _options.User : _options.From));
        message.To.Add(MailboxAddress.Parse(mail.To));
        message.Subject = mail.Subject;
        message.Body = new BodyBuilder { HtmlBody = mail.HtmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var socket = _options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(_options.Host, _options.Port, socket, ct);
        await client.AuthenticateAsync(_options.User, _options.Password, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
        return true;
    }
}

/// <summary>
/// Drains the outbox. A failed send is retried twice with a pause, then logged and
/// dropped: the submission itself is safe in the database either way.
/// </summary>
public sealed class MailSenderService(
    ChannelMailOutbox outbox,
    IMailTransport transport,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<MailSenderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var mail in outbox.Reader.ReadAllAsync(stoppingToken))
        {
            await DeliverAsync(mail, stoppingToken);
        }
    }

    private async Task DeliverAsync(OutgoingMail mail, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (!await transport.SendAsync(mail, ct))
                {
                    return;
                }

                logger.LogInformation("Sent '{Subject}' to {To}.", mail.Subject, mail.To);
                await RecordAsync(mail, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending '{Subject}' to {To} failed (attempt {Attempt}/3).",
                    mail.Subject, mail.To, attempt);

                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10 * attempt), clock, ct);
                }
            }
        }
    }

    private async Task RecordAsync(OutgoingMail mail, CancellationToken ct)
    {
        if (mail.RecordOn is not { } target)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SperantaDbContext>();
        db.Activity.Add(new ActivityEntry
        {
            Kind = target.Kind,
            SubmissionId = target.Id,
            Text = $"Email trimis la {mail.To}",
            At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }
}
