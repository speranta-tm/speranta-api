using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Speranta.Api.Contracts;
using Speranta.Api.Setup;
using Speranta.Api.Storage;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure;

namespace Speranta.Api.Endpoints;

/// <summary>The content an admin can change: announcements, schedule, flyer, books.</summary>
public static class AdminContentEndpoints
{
    public static RouteGroupBuilder MapAdminContent(this RouteGroupBuilder admin)
    {
        // ---- Announcements -------------------------------------------------------
        admin.MapGet("/announcements", async (SperantaDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var rows = await db.Announcements.AsNoTracking()
                .OrderByDescending(a => a.Status == AnnouncementStatus.Draft)
                .ThenByDescending(a => a.PublishAt ?? a.UpdatedAt)
                .ToListAsync(ct);

            return Results.Ok(rows.Select(a => new AdminAnnouncementRow(
                a.Id, a.Slug, a.Title, a.Status, a.PublishAt, IsLive(a, now),
                a.CoverFileId is { } f ? FileStorage.UrlFor(f) : null, a.UpdatedAt)));
        })
            .WithSummary("Every announcement: drafts first, then by publish date.");

        admin.MapGet("/announcements/{id:int}", async (int id, SperantaDbContext db, CancellationToken ct) =>
            await db.Announcements.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) is { } a
                ? Results.Ok(ToAdmin(a))
                : Results.NotFound());

        admin.MapPost("/announcements", async (
            [FromBody] SaveAnnouncementRequest req, SperantaDbContext db, TimeProvider clock, HttpContext http,
            CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var a = new Announcement { Title = "", Slug = "", CreatedAt = now };

            if (await Apply(a, req, db, now, ct) is { } problem)
            {
                return problem;
            }

            db.Announcements.Add(a);
            await db.SaveChangesAsync(ct);
            return CreatedAt.Path(http, $"/admin/announcements/{a.Id}", ToAdmin(a));
        })
            .WithSummary("Create an announcement (a draft, or published/scheduled straight away).");

        admin.MapPut("/announcements/{id:int}", async (
            int id, [FromBody] SaveAnnouncementRequest req, SperantaDbContext db, TimeProvider clock,
            CancellationToken ct) =>
        {
            var a = await db.Announcements.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null)
            {
                return Results.NotFound();
            }

            if (await Apply(a, req, db, clock.GetUtcNow(), ct) is { } problem)
            {
                return problem;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToAdmin(a));
        })
            .WithSummary("Save the whole editor state; this is also what autosave sends.");

        admin.MapDelete("/announcements/{id:int}", async (int id, SperantaDbContext db, CancellationToken ct) =>
            await db.Announcements.Where(a => a.Id == id).ExecuteDeleteAsync(ct) == 0
                ? Results.NotFound()
                : Results.NoContent());

        admin.MapPost("/files/images", async (
            IFormFile file, FileStorage storage, HttpContext http, CancellationToken ct) =>
        {
            var (stored, error) = await storage.SaveAsync(file, UploadKind.Image, ct);
            return stored is null
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [error!] })
                : CreatedAt.Path(http, FileStorage.UrlFor(stored.Id), ToDto(stored));
        })
            .DisableAntiforgery() // bearer tokens, not cookies: there is no ambient credential to forge
            .WithSummary("Upload an announcement cover (JPG, PNG or WebP, up to 5 MB).");

        // ---- Schedule ------------------------------------------------------------
        admin.MapGet("/schedule", async (SperantaDbContext db, CancellationToken ct) =>
        {
            var site = await PublicEndpoints.SiteInfoAsync(db, ct);
            return Results.Ok(new ScheduleDto(site.Services, site.KidsGroups, site.KidsPhone));
        });

        admin.MapPut("/schedule", async ([FromBody] ScheduleDto req, SperantaDbContext db, CancellationToken ct) =>
        {
            var v = new Validation();

            for (var i = 0; i < req.Services.Count; i++)
            {
                var s = req.Services[i];
                if (!Enum.IsDefined(s.Day))
                {
                    v.Add($"services[{i}].day", "Alegeți ziua.");
                }

                if (s.End <= s.Start)
                {
                    v.Add($"services[{i}].end", "Ora de sfârșit trebuie să fie după cea de început.");
                }
            }

            for (var i = 0; i < req.KidsGroups.Count; i++)
            {
                var g = req.KidsGroups[i];
                v.Text($"kidsGroups[{i}].label", g.Label, 100, true, "Scrieți numele grupei.");
                if (g.End <= g.Start)
                {
                    v.Add($"kidsGroups[{i}].end", "Ora de sfârșit trebuie să fie după cea de început.");
                }
            }

            var phone = v.Phone("kidsPhone", req.KidsPhone, required: true);

            if (!v.IsValid)
            {
                return v.Problem();
            }

            // Replace both lists whole: the editor always sends everything, and
            // diffing rows by position would only invent identities they don't have.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.ServiceTimes.ExecuteDeleteAsync(ct);
            await db.KidsGroups.ExecuteDeleteAsync(ct);

            db.ServiceTimes.AddRange(req.Services.Select((s, i) =>
                new ServiceTime { Day = s.Day, Start = s.Start, End = s.End, SortOrder = i + 1 }));
            db.KidsGroups.AddRange(req.KidsGroups.Select((g, i) =>
                new KidsGroup { Label = g.Label.Trim(), Start = g.Start, End = g.End, SortOrder = i + 1 }));

            var settings = await db.Settings.FirstAsync(ct);
            settings.KidsPhone = phone!;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var site = await PublicEndpoints.SiteInfoAsync(db, ct);
            return Results.Ok(new ScheduleDto(site.Services, site.KidsGroups, site.KidsPhone));
        })
            .WithSummary("Replace the service times, the kids' groups and the kids' phone number.");

        // ---- Flyer ---------------------------------------------------------------
        admin.MapGet("/flyer", async (SperantaDbContext db, CancellationToken ct) =>
        {
            var s = await db.Settings.AsNoTracking().Include(x => x.FlyerFile).FirstAsync(ct);
            return s.FlyerFile is { } f
                ? Results.Ok(new AdminFlyer(FileStorage.UrlFor(f.Id), f.FileName, f.CreatedAt, f.SizeBytes, s.FlyerDownloads))
                : Results.NoContent();
        });

        admin.MapPost("/flyer", async (IFormFile file, FileStorage storage, SperantaDbContext db, CancellationToken ct) =>
        {
            var (stored, error) = await storage.SaveAsync(file, UploadKind.Pdf, ct);
            if (stored is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [error!] });
            }

            var settings = await db.Settings.FirstAsync(ct);
            settings.FlyerFileId = stored.Id;
            settings.FlyerDownloads = 0;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new AdminFlyer(
                FileStorage.UrlFor(stored.Id), stored.FileName, stored.CreatedAt, stored.SizeBytes, 0));
        })
            .DisableAntiforgery()
            .WithSummary("Upload this month's flyer (PDF, up to 20 MB). The site's button switches to it at once.");

        // ---- Books ---------------------------------------------------------------
        admin.MapGet("/books", async (SperantaDbContext db, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(await AdminBooks(db, clock, ct)));

        admin.MapPatch("/books/{id:int}", async (
            int id, [FromBody] UpdateBookRequest req, SperantaDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var updated = await db.Books.Where(b => b.Id == id)
                .ExecuteUpdateAsync(b => b.SetProperty(x => x.IsAvailable, req.IsAvailable), ct);
            return updated == 0 ? Results.NotFound() : Results.Ok(await AdminBooks(db, clock, ct));
        })
            .WithSummary("Switch a book on or off. Off shows 'Stoc epuizat' and refuses new orders.");

        admin.MapPut("/books/order", async (
            [FromBody] ReorderBooksRequest req, SperantaDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var books = await db.Books.ToListAsync(ct);
            if (req.Ids.Count != books.Count || !books.All(b => req.Ids.Contains(b.Id)))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ids"] = ["Trimiteți fiecare carte exact o dată."],
                });
            }

            foreach (var b in books)
            {
                b.SortOrder = req.Ids.ToList().IndexOf(b.Id) + 1;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(await AdminBooks(db, clock, ct));
        })
            .WithSummary("Set the order the books appear in on the site.");

        return admin;
    }

    private static async Task<IResult?> Apply(
        Announcement a, SaveAnnouncementRequest req, SperantaDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var v = new Validation();
        var title = v.Text("title", req.Title, 200, true, "Scrieți un titlu.");
        var body = Html.Sanitize(req.BodyHtml);

        if (body.Length > Limits.AnnouncementBody)
        {
            v.Add("bodyHtml", "Anunțul este prea lung.");
        }

        if (req.Status == AnnouncementStatus.Published && Html.Excerpt(body).Length == 0)
        {
            v.Add("bodyHtml", "Un anunț publicat are nevoie de text.");
        }

        if (req.CoverFileId is { } fileId
            && !await db.Files.AnyAsync(f => f.Id == fileId && f.ContentType.StartsWith("image/"), ct))
        {
            v.Add("coverFileId", "Imaginea nu există.");
        }

        if (!v.IsValid)
        {
            return v.Problem();
        }

        a.Title = title!;
        a.BodyHtml = body;
        a.CoverFileId = req.CoverFileId;
        a.ShowOnHome = req.ShowOnHome;
        a.Status = req.Status;
        a.PublishAt = req.Status == AnnouncementStatus.Published ? req.PublishAt ?? a.PublishAt ?? now : req.PublishAt;
        a.UpdatedAt = now;

        // The address follows the title until the first publish, then freezes.
        if (a.FirstPublishedAt is null)
        {
            a.Slug = await UniqueSlug(db, Slug.From(a.Title), a.Id, ct);
        }

        if (a.Status == AnnouncementStatus.Published)
        {
            a.FirstPublishedAt ??= now;
        }

        return null;
    }

    private static async Task<string> UniqueSlug(SperantaDbContext db, string baseSlug, int selfId, CancellationToken ct)
    {
        var slug = baseSlug;
        for (var n = 2; await db.Announcements.AnyAsync(a => a.Slug == slug && a.Id != selfId, ct); n++)
        {
            slug = $"{baseSlug}-{n}";
        }

        return slug;
    }

    private static bool IsLive(Announcement a, DateTimeOffset now) =>
        a.Status == AnnouncementStatus.Published && a.PublishAt is { } at && at <= now;

    private static AdminAnnouncement ToAdmin(Announcement a) => new(
        a.Id, a.Slug, a.Title, a.BodyHtml, a.CoverFileId,
        a.CoverFileId is { } f ? FileStorage.UrlFor(f) : null,
        a.Status, a.PublishAt, a.ShowOnHome, a.CreatedAt, a.UpdatedAt);

    private static FileDto ToDto(StoredFile f) =>
        new(f.Id, FileStorage.UrlFor(f.Id), f.FileName, f.ContentType, f.SizeBytes);

    private static async Task<List<AdminBook>> AdminBooks(SperantaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var monthStart = LocalTime.StartOfMonth(clock.GetUtcNow());
        return await db.Books.AsNoTracking().OrderBy(b => b.SortOrder)
            .Select(b => new AdminBook(b.Id, b.Slug, b.Title, b.IsAvailable, b.SortOrder,
                db.BookOrders.Count(o => o.BookId == b.Id && o.CreatedAt >= monthStart)))
            .ToListAsync(ct);
    }
}
