using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Speranta.Api.Setup;

/// <summary>
/// Collects field errors in the shape <c>Results.ValidationProblem</c> wants, with
/// the Romanian messages the forms show. Keys are the camelCase field names the
/// web client posts, so it can put each message under the right input.
/// </summary>
public sealed partial class Validation
{
    private readonly Dictionary<string, string[]> _errors = [];

    public bool IsValid => _errors.Count == 0;

    public IResult Problem() => Results.ValidationProblem(_errors);

    public void Add(string field, string message) => _errors.TryAdd(field, [message]);

    /// <summary>Trimmed text, or null when blank. Records an error when required or too long.</summary>
    public string? Text(string field, string? value, int max, bool required, string requiredMessage)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        if (trimmed is null)
        {
            if (required)
            {
                Add(field, requiredMessage);
            }

            return null;
        }

        if (trimmed.Length > max)
        {
            Add(field, $"Maximum {max} de caractere.");
        }

        return trimmed;
    }

    public string? Phone(string field, string? value, bool required)
    {
        var phone = Text(field, value, Infrastructure.Limits.Phone, required, "Scrieți un număr de telefon.");
        if (phone is not null && !PhonePattern().IsMatch(phone))
        {
            Add(field, "Numărul de telefon nu pare corect.");
        }

        return phone;
    }

    public string? Email(string field, string? value, bool required)
    {
        var email = Text(field, value, Infrastructure.Limits.Email, required, "Scrieți adresa de email.");
        if (email is not null && !IsEmail(email))
        {
            Add(field, "Adresa de email nu pare corectă.");
        }

        return email;
    }

    public void MustAccept(string field, bool accepted, string message)
    {
        if (!accepted)
        {
            Add(field, message);
        }
    }

    public static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var parsed)
        && parsed.Address == value
        && value.Contains('.', StringComparison.Ordinal);

    // 9–15 digits, allowing spaces, dots, dashes, brackets and a leading +.
    [GeneratedRegex(@"^\+?[\d\s().-]{9,20}$")]
    private static partial Regex PhonePattern();
}
