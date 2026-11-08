using System.Net;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Speranta.Api.Contracts;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure.Identity;

namespace Speranta.Api.Email;

/// <summary>
/// The emails the site sends. Everything a visitor typed is HTML-encoded: these
/// bodies are HTML, and a form field is not allowed to become markup in the
/// church's inbox.
/// </summary>
public sealed class Notifications(IMailOutbox outbox, IOptions<EmailOptions> options)
{
    private readonly EmailOptions _options = options.Value;

    public Task NewBookOrderAsync(BookOrder order, Book book, CancellationToken ct) => NotifyAsync(ct,
        SubmissionKind.BookOrder, order.Id,
        $"Comandă nouă: {book.Title} – {order.FullName}",
        $"Comandă nouă #{order.Id}",
        [
            ("Carte", book.Title),
            ("Nume", order.FullName),
            ("Telefon", order.Phone),
            ("Primire", Labels.Of(order.DeliveryMethod)),
            ("Adresă", order.Address ?? "—"),
            ("Grupa de vârstă", order.AgeGroup is { } age ? Labels.Of(age) : "—"),
            ("Are Biblie acasă", order.HasBibleAtHome is { } has ? Labels.YesNo(has) : "—"),
        ],
        $"admin/comenzi/{order.Id}");

    public Task NewKidsSignupAsync(KidsSignup signup, CancellationToken ct) => NotifyAsync(ct,
        SubmissionKind.KidsSignup, signup.Id,
        $"Înscriere nouă la activitățile pentru copii – {signup.ParentName}",
        "Înscriere nouă la activitățile pentru copii",
        [
            ("Părinte", signup.ParentName),
            ("Telefon", signup.Phone),
            ("Email", signup.Email ?? "—"),
            ("Copil", ChildText(signup)),
            ("A mai participat", Labels.YesNo(signup.ParticipatedBefore)),
            ("Mesaj", signup.Message ?? "—"),
        ],
        $"admin/inscrieri/{signup.Id}");

    public Task NewContactMessageAsync(ContactMessage message, CancellationToken ct)
    {
        var name = string.Join(' ', new[] { message.FirstName, message.LastName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        return NotifyAsync(ct,
            SubmissionKind.ContactMessage, message.Id,
            $"Mesaj nou de pe site: {message.Subject ?? "(fără subiect)"}",
            "Mesaj nou de pe site",
            [
                ("De la", string.IsNullOrEmpty(name) ? message.Email : $"{name} <{message.Email}>"),
                ("Subiect", message.Subject ?? "—"),
                ("Mesaj", message.Body),
            ],
            $"admin/mesaje/{message.Id}");
    }

    public Task AdminInviteAsync(string email, string token, string? invitedBy, CancellationToken ct)
    {
        var link = Link($"admin/invitatie?token={Uri.EscapeDataString(token)}");
        var who = string.IsNullOrWhiteSpace(invitedBy) ? "Cineva" : WebUtility.HtmlEncode(invitedBy);

        return outbox.EnqueueAsync(new OutgoingMail(email, "Invitație: administrare site Speranța Timișoara",
            Wrap($"<p>Bună,</p><p>{who} te-a invitat să administrezi site-ul bisericii "
                + "Speranța Timișoara.</p>"
                + $"<p><a href=\"{link}\">Acceptă invitația și alege o parolă</a></p>"
                + "<p>Linkul este valabil 7 zile. Dacă nu te așteptai la acest email, îl poți ignora.</p>")), ct);
    }

    private static string ChildText(KidsSignup s) => (s.ChildFirstName, s.ChildAge) switch
    {
        (null, null) => "—",
        ({ } n, null) => n,
        (null, { } a) => $"{a} ani",
        ({ } n, { } a) => $"{n}, {a} ani",
    };

    private Task NotifyAsync(
        CancellationToken ct, SubmissionKind kind, int id, string subject, string heading,
        (string Label, string Value)[] rows, string adminPath)
    {
        var html = new StringBuilder();
        html.Append($"<h2 style=\"margin:0 0 12px\">{WebUtility.HtmlEncode(heading)}</h2>");
        html.Append("<table cellpadding=\"6\" style=\"border-collapse:collapse\">");

        foreach (var (label, value) in rows)
        {
            html.Append("<tr><td style=\"color:#4A5568;vertical-align:top\">")
                .Append(WebUtility.HtmlEncode(label))
                .Append("</td><td style=\"white-space:pre-wrap\">")
                .Append(WebUtility.HtmlEncode(value))
                .Append("</td></tr>");
        }

        html.Append("</table>");
        html.Append($"<p><a href=\"{Link(adminPath)}\">Deschide în panoul de administrare</a></p>");

        return outbox.EnqueueAsync(new OutgoingMail(_options.NotifyTo, subject, Wrap(html.ToString()), (kind, id)), ct);
    }

    private string Link(string path) => $"{_options.WebUrl.TrimEnd('/')}/{path}";

    private static string Wrap(string inner) =>
        "<div style=\"font-family:Inter,Arial,sans-serif;font-size:15px;color:#1A202C;line-height:1.5\">"
        + inner
        + "<p style=\"color:#718096;font-size:13px\">Biserica Adventistă de Ziua a Șaptea Speranța Timișoara</p>"
        + "</div>";
}

/// <summary>
/// Identity's own emails (password reset). Registering a sender is what makes
/// /auth/forgotPassword actually deliver; without one it succeeds and sends nothing.
/// Transient, because MapIdentityApi resolves it from the root provider.
/// </summary>
public sealed class IdentityEmailSender(IServiceScopeFactory scopes, IOptions<EmailOptions> options)
    : IEmailSender<AppUser>
{
    private readonly EmailOptions _options = options.Value;

    // Admins are created by invitation with a confirmed address, so this is never asked for.
    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) =>
        Task.CompletedTask;

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) =>
        SendPasswordResetCodeAsync(user, email, resetLink);

    public async Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
    {
        var link = $"{_options.WebUrl.TrimEnd('/')}/admin/resetare-parola"
            + $"?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(resetCode)}";

        await using var scope = scopes.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IMailOutbox>();
        await outbox.EnqueueAsync(new OutgoingMail(email, "Resetare parolă – site Speranța Timișoara",
            "<div style=\"font-family:Inter,Arial,sans-serif;font-size:15px;color:#1A202C\">"
            + $"<p>Bună,</p><p><a href=\"{link}\">Alege o parolă nouă</a>.</p>"
            + "<p>Dacă nu ai cerut tu asta, ignoră acest email.</p></div>"), CancellationToken.None);
    }
}
