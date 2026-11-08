using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;

namespace Speranta.Api.Setup;

/// <summary>
/// Announcement bodies come from the admin editor as HTML and are rendered as-is
/// on the public site, so they are cut down to what the editor can produce:
/// paragraphs, small headings, lists, links, bold and italic. Anything else (a
/// script, an iframe, an onclick, a style) is dropped. An admin account is not a
/// reason to trust markup; it is a reason someone would want to steal one.
/// </summary>
public static partial class Html
{
    private static readonly HtmlSanitizer Sanitizer = Create();

    public static string Sanitize(string? html) =>
        string.IsNullOrWhiteSpace(html) ? "" : Sanitizer.Sanitize(html).Trim();

    /// <summary>Plain text of the first ~<paramref name="max"/> characters, for list cards.</summary>
    public static string Excerpt(string html, int max = 220)
    {
        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        text = Spaces().Replace(text, " ").Trim();

        if (text.Length <= max)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', max);
        return text[..(cut > max / 2 ? cut : max)].TrimEnd(',', '.', ';', ':') + "…";
    }

    private static HtmlSanitizer Create()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "h2", "h3", "ul", "ol", "li", "a", "strong", "b", "em", "i", "blockquote" })
        {
            s.AllowedTags.Add(tag);
        }

        s.AllowedAttributes.Clear();
        s.AllowedAttributes.Add("href");
        s.AllowedCssProperties.Clear();
        s.AllowedAtRules.Clear();
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("mailto");
        s.AllowedSchemes.Add("tel");

        // External links open safely; the site's own links stay as they are.
        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a
                && a.GetAttribute("href") is { } href
                && href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                a.SetAttribute("target", "_blank");
                a.SetAttribute("rel", "noopener noreferrer");
            }
        };

        s.AllowedAttributes.Add("target");
        s.AllowedAttributes.Add("rel");
        return s;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
