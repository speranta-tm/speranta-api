using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Speranta.Api.Contracts;
using Speranta.Api.Setup;
using Speranta.Api.Storage;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure;

namespace Speranta.Api.Endpoints;

/// <summary>What the public site reads: schedule, flyer, books, announcements, files.</summary>
public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicContent(this IEndpointRouteBuilder app)
    {
        app.MapGet("/site", async (SperantaDbContext db, CancellationToken ct) =>
            Results.Ok(await SiteInfoAsync(db, ct)))
            .WithSummary("Schedule, kids' groups, the current flyer and the book list, for the header, footer and pages.");

        app.MapGet("/announcements", async (
            SperantaDbContext db, TimeProvider clock, int? page, int? pageSize, bool? home,
            CancellationToken ct) =>
        {
            var size = Math.Clamp(pageSize ?? 9, 1, 50);
            var number = Math.Max(page ?? 1, 1);
            var query = Live(db, clock.GetUtcNow());

            if (home == true)
            {
                query = query.Where(a => a.ShowOnHome);
            }

            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(a => a.PublishAt)
                .Skip((number - 1) * size).Take(size)
                .Select(a => new { a.Slug, a.Title, a.BodyHtml, a.CoverFileId, a.PublishAt })
                .ToListAsync(ct);

            var items = rows.Select(a => new AnnouncementSummary(
                a.Slug, a.Title, Html.Excerpt(a.BodyHtml),
                a.CoverFileId is { } f ? FileStorage.UrlFor(f) : null, a.PublishAt!.Value)).ToList();

            return Results.Ok(new Page<AnnouncementSummary>(items, total, number, size));
        })
            .WithSummary("Published announcements, newest first. home=true keeps the ones marked for the home page.");

        app.MapGet("/announcements/{slug}", async (
            string slug, SperantaDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var a = await Live(db, clock.GetUtcNow()).FirstOrDefaultAsync(x => x.Slug == slug, ct);
            return a is null
                ? Results.NotFound()
                : Results.Ok(new AnnouncementDetail(
                    a.Slug, a.Title, a.BodyHtml,
                    a.CoverFileId is { } f ? FileStorage.UrlFor(f) : null, a.PublishAt!.Value));
        })
            .WithSummary("One published announcement. Drafts and scheduled ones are 404 here.");

        app.MapGet("/files/{id:guid}", async (
            Guid id, SperantaDbContext db, FileStorage storage, CancellationToken ct) =>
        {
            var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            var stream = file is null ? null : storage.Open(id);

            return stream is null
                ? Results.NotFound()
                : Results.File(stream, file!.ContentType, file.FileName,
                    lastModified: file.CreatedAt,
                    entityTag: new EntityTagHeaderValue($"\"{id:N}\""),
                    enableRangeProcessing: true);
        })
            .WithSummary("An uploaded file (announcement cover, flyer). Immutable: a new upload gets a new id.");

        // The header's "Descarcă pliantul lunar" button points here, so the link on the
        // site never changes while the file behind it does. Counting happens here too.
        app.MapGet("/flyer", async (SperantaDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var settings = await db.Settings.FirstAsync(ct);
            if (settings.FlyerFileId is not { } fileId)
            {
                return Results.NotFound();
            }

            await db.Settings.Where(s => s.Id == settings.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FlyerDownloads, x => x.FlyerDownloads + 1), ct);

            return Results.Redirect(http.Request.PathBase.Add(FileStorage.UrlFor(fileId)).Value!);
        })
            .WithSummary("Redirects to the current monthly flyer and counts the download.");

        return app;
    }

    public static IQueryable<Announcement> Live(SperantaDbContext db, DateTimeOffset now) =>
        db.Announcements.AsNoTracking()
            .Where(a => a.Status == AnnouncementStatus.Published && a.PublishAt != null && a.PublishAt <= now);

    public static async Task<SiteInfo> SiteInfoAsync(SperantaDbContext db, CancellationToken ct)
    {
        var services = await db.ServiceTimes.AsNoTracking().OrderBy(s => s.SortOrder)
            .Select(s => new ServiceTimeDto(s.Day, s.Start, s.End)).ToListAsync(ct);
        var groups = await db.KidsGroups.AsNoTracking().OrderBy(g => g.SortOrder)
            .Select(g => new KidsGroupDto(g.Label, g.Start, g.End)).ToListAsync(ct);
        var books = await db.Books.AsNoTracking().OrderBy(b => b.SortOrder)
            .Select(b => new BookDto(b.Slug, b.Title, b.IsAvailable)).ToListAsync(ct);
        var settings = await db.Settings.AsNoTracking().Include(s => s.FlyerFile).FirstAsync(ct);

        var flyer = settings.FlyerFile is { } f
            ? new FlyerDto("/flyer", f.FileName, f.CreatedAt, f.SizeBytes)
            : null;

        return new SiteInfo(services, groups, settings.KidsPhone, flyer, books);
    }
}
