using System.Globalization;
using System.Text;

namespace Speranta.Domain;

public static class Slug
{
    private const int MaxLength = 80;

    /// <summary>"Program special de sărbători" → "program-special-de-sarbatori".</summary>
    public static string From(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var pendingDash = false;

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingDash && sb.Length > 0)
                {
                    sb.Append('-');
                }

                sb.Append(char.ToLowerInvariant(c));
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = sb.Length > MaxLength ? sb.ToString(0, MaxLength).TrimEnd('-') : sb.ToString();
        return slug.Length == 0 ? "anunt" : slug;
    }
}

public static class KidsAge
{
    /// <summary>The site's two groups: under 7, and 7 to 14. Null outside both or unknown.</summary>
    public static string? GroupFor(int? age) => age switch
    {
        null => null,
        < 7 => "UnderSeven",
        <= 14 => "SevenToFourteen",
        _ => null,
    };
}
