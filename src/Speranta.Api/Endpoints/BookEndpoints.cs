using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Speranta.Api.Setup;
using Speranta.Api.Storage;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure;

namespace Speranta.Api.Endpoints;

/// <summary>Everything a book's page shows.</summary>
public sealed record BookPage(
    string Slug, string Title, string Heading, string Blurb, string Lead,
    IReadOnlyList<string> Paragraphs, IReadOnlyList<string> Benefits, string? Closing,
    string FormEyebrow, string FormTitle, string? CoverUrl, string? BannerUrl, bool IsAvailable,
    bool AsksAgeGroup, bool AsksBibleAtHome, bool AllowsPickup, bool IsForChildren);

/// <summary>The admin's book editor, in both directions.</summary>
public sealed record AdminBookDetail(
    int Id, string Slug, string Title, string? Heading, string Blurb, string Lead,
    IReadOnlyList<string> Paragraphs, IReadOnlyList<string> Benefits, string? Closing,
    string FormEyebrow, string FormTitle, Guid? CoverFileId, string? CoverUrl, Guid? BannerFileId, string? BannerUrl,
    bool IsAvailable, bool AsksAgeGroup, bool AsksBibleAtHome, bool AllowsPickup, bool IsForChildren, int OrderCount);

public sealed record SaveBookRequest(
    string? Title, string? Heading, string? Blurb, string? Lead, IReadOnlyList<string>? Paragraphs,
    IReadOnlyList<string>? Benefits, string? Closing, string? FormEyebrow, string? FormTitle,
    Guid? CoverFileId, Guid? BannerFileId, bool IsAvailable, bool AsksAgeGroup, bool AsksBibleAtHome,
    bool AllowsPickup, bool IsForChildren);

public static class BookEndpoints
{
    public static IEndpointRouteBuilder MapBookPages(this IEndpointRouteBuilder app)
    {
        app.MapGet("/books/{slug}", async (string slug, SperantaDbContext db, CancellationToken ct) =>
            await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Slug == slug, ct) is { } b
                ? Results.Ok(new BookPage(b.Slug, b.Title, b.Heading ?? b.Title, b.Blurb, b.Lead, b.Paragraphs, b.Benefits,
                    b.Closing, b.FormEyebrow, b.FormTitle, Url(b.CoverFileId), Url(b.BannerFileId ?? b.CoverFileId),
                    b.IsAvailable, b.AsksAgeGroup, b.AsksBibleAtHome, b.AllowsPickup, b.IsForChildren))
                : Results.NotFound())
            .WithSummary("One book's page. Covers are null for the launch books, whose images ship with the web client.");

        return app;
    }

    public static RouteGroupBuilder MapAdminBookEditor(this RouteGroupBuilder admin)
    {
        admin.MapGet("/books/{id:int}", async (int id, SperantaDbContext db, CancellationToken ct) =>
            await Detail(db, id, ct) is { } d ? Results.Ok(d) : Results.NotFound());

        admin.MapPost("/books", async ([FromBody] SaveBookRequest req, SperantaDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var book = new Book { Slug = "", Title = "" };
            if (await Apply(book, req, db, isNew: true, ct) is { } problem)
            {
                return problem;
            }

            book.Slug = await UniqueSlug(db, Slug.From(book.Title), ct);
            book.SortOrder = (await db.Books.MaxAsync(b => (int?)b.SortOrder, ct) ?? 0) + 1;
            db.Books.Add(book);
            await db.SaveChangesAsync(ct);
            return CreatedAt.Path(http, $"/admin/books/{book.Id}", await Detail(db, book.Id, ct));
        })
            .WithSummary("Add a book. Its page appears at /carti-gratuite/{slug}, last in the list.");

        admin.MapPut("/books/{id:int}", async (int id, [FromBody] SaveBookRequest req, SperantaDbContext db, CancellationToken ct) =>
        {
            var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (book is null)
            {
                return Results.NotFound();
            }

            // The address stays as it was: links to the book are already out there.
            if (await Apply(book, req, db, isNew: false, ct) is { } problem)
            {
                return problem;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(await Detail(db, id, ct));
        })
            .WithSummary("Edit a book's page and its order form. The address (slug) does not change.");

        admin.MapDelete("/books/{id:int}", async (int id, SperantaDbContext db, CancellationToken ct) =>
        {
            var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (book is null)
            {
                return Results.NotFound();
            }

            // Orders keep pointing at their book; removing it would lose what was ordered.
            if (await db.BookOrders.AnyAsync(o => o.BookId == id, ct))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["id"] = ["Cartea are deja comenzi, așa că nu se poate șterge. Oprește-o: pe site apare „Stoc epuizat”."],
                });
            }

            db.Books.Remove(book);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
            .WithSummary("Delete a book that has never been ordered.");

        return admin;
    }

    private static string? Url(Guid? id) => id is { } f ? FileStorage.UrlFor(f) : null;

    private static async Task<AdminBookDetail?> Detail(SperantaDbContext db, int id, CancellationToken ct)
    {
        var b = await db.Books.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b is null)
        {
            return null;
        }

        var orders = await db.BookOrders.CountAsync(o => o.BookId == id, ct);
        return new AdminBookDetail(b.Id, b.Slug, b.Title, b.Heading, b.Blurb, b.Lead, b.Paragraphs, b.Benefits, b.Closing,
            b.FormEyebrow, b.FormTitle, b.CoverFileId, Url(b.CoverFileId), b.BannerFileId, Url(b.BannerFileId),
            b.IsAvailable, b.AsksAgeGroup, b.AsksBibleAtHome, b.AllowsPickup, b.IsForChildren, orders);
    }

    private static async Task<IResult?> Apply(Book book, SaveBookRequest req, SperantaDbContext db, bool isNew, CancellationToken ct)
    {
        var v = new Validation();
        var title = v.Text("title", req.Title, 200, true, "Scrie titlul cărții.");
        var heading = v.Text("heading", req.Heading, 200, false, "");
        var blurb = v.Text("blurb", req.Blurb, 300, true, "Scrie o frază pentru pagina „Cărți gratuite”.");
        var lead = v.Text("lead", req.Lead, 1000, true, "Scrie introducerea.");
        var closing = v.Text("closing", req.Closing, 300, false, "");
        var eyebrow = v.Text("formEyebrow", req.FormEyebrow, 100, false, "");
        var formTitle = v.Text("formTitle", req.FormTitle, 200, false, "");

        var paragraphs = Clean(req.Paragraphs, 20, 3000, "paragraphs", "paragrafe", v);
        var benefits = Clean(req.Benefits, 10, 300, "benefits", "puncte", v);

        if (isNew && req.CoverFileId is null)
        {
            v.Add("coverFileId", "Adaugă coperta cărții.");
        }

        foreach (var (field, id) in new[] { ("coverFileId", req.CoverFileId), ("bannerFileId", req.BannerFileId) })
        {
            if (id is { } f && !await db.Files.AnyAsync(x => x.Id == f && x.ContentType.StartsWith("image/"), ct))
            {
                v.Add(field, "Imaginea nu există. Încarc-o din nou.");
            }
        }

        if (!v.IsValid)
        {
            return v.Problem();
        }

        book.Title = title!;
        book.Heading = heading == title ? null : heading;
        book.Blurb = blurb!;
        book.Lead = lead!;
        book.Paragraphs = paragraphs;
        book.Benefits = benefits;
        book.Closing = closing;
        book.FormEyebrow = eyebrow ?? "Comandă cartea";
        book.FormTitle = formTitle ?? $"Primește gratuit „{title}”";
        // A launch book keeps its shipped cover until someone uploads a new one.
        book.CoverFileId = req.CoverFileId ?? (isNew ? null : book.CoverFileId);
        book.BannerFileId = req.BannerFileId;
        book.IsAvailable = req.IsAvailable;
        book.AsksAgeGroup = req.AsksAgeGroup;
        book.AsksBibleAtHome = req.AsksBibleAtHome;
        book.AllowsPickup = req.AllowsPickup;
        book.IsForChildren = req.IsForChildren;
        return null;
    }

    private static List<string> Clean(IReadOnlyList<string>? items, int maxCount, int maxLength, string field, string what, Validation v)
    {
        var list = (items ?? []).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (list.Count > maxCount)
        {
            v.Add(field, $"Cel mult {maxCount} {what}.");
        }

        if (list.Any(s => s.Length > maxLength))
        {
            v.Add(field, $"Fiecare poate avea cel mult {maxLength} de caractere.");
        }

        return list;
    }

    private static async Task<string> UniqueSlug(SperantaDbContext db, string baseSlug, CancellationToken ct)
    {
        var slug = baseSlug == "anunt" ? "carte" : baseSlug;
        var root = slug;
        for (var n = 2; await db.Books.AnyAsync(b => b.Slug == slug, ct); n++)
        {
            slug = $"{root}-{n}";
        }

        return slug;
    }
}
