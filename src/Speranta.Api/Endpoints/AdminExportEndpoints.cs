using System.Text;
using Microsoft.EntityFrameworkCore;
using Speranta.Api.Contracts;
using Speranta.Api.Setup;
using Speranta.Domain;
using Speranta.Infrastructure;

namespace Speranta.Api.Endpoints;

/// <summary>
/// "Exportă CSV" on the orders and sign-ups lists: the same filter as the list, as a
/// file Excel opens correctly (UTF-8 with BOM for the diacritics, ";" because a
/// Romanian Excel expects it).
/// </summary>
public static class AdminExportEndpoints
{
    public static RouteGroupBuilder MapAdminExports(this RouteGroupBuilder admin)
    {
        admin.MapGet("/book-orders.csv", async (
            SperantaDbContext db, TimeProvider clock, OrderStatus? status, string? q, CancellationToken ct) =>
        {
            var query = db.BookOrders.AsNoTracking();
            if (status is { } s)
            {
                query = query.Where(o => o.Status == s);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(o => EF.Functions.ILike(o.FullName, term) || EF.Functions.ILike(o.Phone, term));
            }

            var rows = await query.OrderByDescending(o => o.CreatedAt)
                .Select(o => new { o.Id, o.CreatedAt, o.FullName, o.Phone, Book = o.Book.Title, o.DeliveryMethod,
                    o.Address, o.AgeGroup, o.HasBibleAtHome, o.Status, o.InternalNote })
                .ToListAsync(ct);

            var csv = Build(
                ["Nr.", "Data", "Nume", "Telefon", "Carte", "Primire", "Adresă", "Grupa de vârstă", "Are Biblie acasă", "Status", "Notă internă"],
                rows.Select(o => new[]
                {
                    o.Id.ToString(), Local(o.CreatedAt), o.FullName, o.Phone, o.Book, Labels.Of(o.DeliveryMethod),
                    o.Address ?? "", o.AgeGroup is { } a ? Labels.Of(a) : "",
                    o.HasBibleAtHome is { } h ? Labels.YesNo(h) : "", Labels.Of(o.Status), o.InternalNote ?? "",
                }));

            return File(csv, "comenzi-carti", clock);
        })
            .WithSummary("Book orders as CSV, with the list's filter.");

        admin.MapGet("/kids-signups.csv", async (
            SperantaDbContext db, TimeProvider clock, SignupStatus? status, string? q, CancellationToken ct) =>
        {
            var query = db.KidsSignups.AsNoTracking();
            if (status is { } s)
            {
                query = query.Where(x => x.Status == s);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(x => EF.Functions.ILike(x.ParentName, term) || EF.Functions.ILike(x.Phone, term)
                    || (x.ChildFirstName != null && EF.Functions.ILike(x.ChildFirstName, term)));
            }

            var rows = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

            var csv = Build(
                ["Nr.", "Data", "Părinte", "Telefon", "Email", "Copil", "Vârsta", "Grupa", "A mai participat", "Mesaj", "Status", "Notă internă"],
                rows.Select(x => new[]
                {
                    x.Id.ToString(), Local(x.CreatedAt), x.ParentName, x.Phone, x.Email ?? "", x.ChildFirstName ?? "",
                    x.ChildAge?.ToString() ?? "", GroupLabel(KidsAge.GroupFor(x.ChildAge)), Labels.YesNo(x.ParticipatedBefore),
                    x.Message ?? "", Labels.Of(x.Status), x.InternalNote ?? "",
                }));

            return File(csv, "inscrieri-copii", clock);
        })
            .WithSummary("Kids' sign-ups as CSV, with the list's filter.");

        return admin;
    }

    private static string GroupLabel(string? group) => group switch
    {
        "UnderSeven" => "Până la 7 ani",
        "SevenToFourteen" => "7–14 ani",
        _ => "",
    };

    private static string Local(DateTimeOffset at) =>
        TimeZoneInfo.ConvertTime(at, LocalTime.Zone).ToString("yyyy-MM-dd HH:mm");

    private static IResult File(string csv, string name, TimeProvider clock)
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        var day = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), LocalTime.Zone).ToString("yyyy-MM-dd");
        return Results.File(bytes, "text/csv; charset=utf-8", $"{name}-{day}.csv");
    }

    internal static string Build(string[] headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(';', headers.Select(Cell)).Append("\r\n");
        foreach (var row in rows)
        {
            sb.AppendJoin(';', row.Select(Cell)).Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quotes when needed, and defuses formula injection: a visitor typing
    /// "=HYPERLINK(...)" as their name must not become a live formula in Excel.
    /// </summary>
    private static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
