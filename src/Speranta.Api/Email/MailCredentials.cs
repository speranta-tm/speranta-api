using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Speranta.Infrastructure;

namespace Speranta.Api.Email;

/// <summary>
/// The SMTP sign-in actually used. Host and user come from configuration (Gmail and
/// the church's address by default); the password is the one an admin saved in the
/// panel if there is one, otherwise Email:Password. Singleton, so a password saved in
/// the admin takes effect for the very next send.
/// </summary>
public sealed class MailCredentials(IOptions<EmailOptions> options)
{
    private readonly EmailOptions _options = options.Value;
    private volatile string? _savedPassword;

    public string Host => _options.Host;

    public int Port => _options.Port;

    public string User => string.IsNullOrWhiteSpace(_options.User) ? _options.NotifyTo : _options.User;

    public string Password => _savedPassword ?? _options.Password;

    /// <summary>Whether the password in use came from the admin panel.</summary>
    public bool FromAdmin => _savedPassword is not null;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);

    public void UseSaved(string? password) => _savedPassword = string.IsNullOrWhiteSpace(password) ? null : password;

    public const string ProtectorPurpose = "Speranta.SmtpPassword";

    /// <summary>Loads the admin-saved password at startup.</summary>
    public static async Task LoadAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SperantaDbContext>();
        var saved = await db.Settings.AsNoTracking().Select(s => s.SmtpPasswordProtected).FirstAsync();
        if (saved is null)
        {
            return;
        }

        try
        {
            var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(ProtectorPurpose);
            scope.ServiceProvider.GetRequiredService<MailCredentials>().UseSaved(protector.Unprotect(saved));
        }
        catch (Exception ex)
        {
            // The Data Protection keys changed (lost key folder): the saved password
            // can't be read. The admin banner will say email is failing, and saving
            // the password again fixes it.
            logger.LogWarning(ex, "The SMTP password saved in the admin could not be decrypted.");
        }
    }
}
