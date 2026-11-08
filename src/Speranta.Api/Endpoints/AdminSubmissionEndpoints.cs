using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Speranta.Api.Contracts;
using Speranta.Api.Email;
using Speranta.Api.Setup;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure;
using Speranta.Infrastructure.Identity;

namespace Speranta.Api.Endpoints;

/// <summary>Book orders, kids' sign-ups and contact messages, as the admin sees them.</summary>
public static class AdminSubmissionEndpoints
{
    public static RouteGroupBuilder MapAdminSubmissions(this RouteGroupBuilder admin)
    {
        // ---- Dashboard -----------------------------------------------------------
        admin.MapGet("/dashboard", async (
            SperantaDbContext db, UserManager<AppUser> users, ClaimsPrincipal principal,
            TimeProvider clock, CancellationToken ct) =>
        {
            var monthStart = LocalTime.StartOfMonth(clock.GetUtcNow());
            var me = await users.GetUserAsync(principal);

            var newOrders = await db.BookOrders
                .Where(o => o.Status == OrderStatus.New).OrderByDescending(o => o.CreatedAt)
                .Select(o => new { o.Id, o.FullName, Book = o.Book.Title, o.DeliveryMethod, o.CreatedAt })
                .ToListAsync(ct);
            var newSignups = await db.KidsSignups
                .Where(s => s.Status == SignupStatus.New).OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.Id, s.ParentName, s.ChildFirstName, s.ChildAge, s.CreatedAt })
                .ToListAsync(ct);
            var newMessages = await db.ContactMessages
                .Where(m => m.Status == MessageStatus.New).OrderByDescending(m => m.CreatedAt)
                .Select(m => new { m.Id, m.FirstName, m.LastName, m.Email, m.Subject, m.CreatedAt })
                .ToListAsync(ct);

            var todo = newOrders.Select(o => new DashboardTodo(
                    SubmissionKind.BookOrder, o.Id, $"{o.FullName} · {o.Book}",
                    o.DeliveryMethod == DeliveryMethod.Pickup ? "ridicare de la biserică" : "livrare", o.CreatedAt))
                .Concat(newSignups.Select(s => new DashboardTodo(
                    SubmissionKind.KidsSignup, s.Id, $"{s.ParentName} · {Child(s.ChildFirstName, s.ChildAge)}",
                    "înscriere copii", s.CreatedAt)))
                .Concat(newMessages.Select(m => new DashboardTodo(
                    SubmissionKind.ContactMessage, m.Id, $"{Name(m.FirstName, m.LastName, m.Email)} · {m.Subject ?? "Mesaj"}",
                    "mesaj", m.CreatedAt)))
                .OrderByDescending(t => t.At)
                .Take(10)
                .ToList();

            return Results.Ok(new Dashboard(
                me?.DisplayName,
                newOrders.Count, await db.BookOrders.CountAsync(o => o.CreatedAt >= monthStart, ct),
                newSignups.Count, await db.KidsSignups.CountAsync(s => s.Status == SignupStatus.Enrolled, ct),
                newMessages.Count, await db.ContactMessages.CountAsync(m => m.CreatedAt >= monthStart, ct),
                todo));
        })
            .WithSummary("Counts for the dashboard cards and the 'De făcut' list (everything still New).");

        // ---- Book orders ---------------------------------------------------------
        admin.MapGet("/book-orders", async (
            SperantaDbContext db, MailHealth mail, TimeProvider clock, OrderStatus? status, string? q, int? page,
            int? pageSize, CancellationToken ct) =>
        {
            var query = db.BookOrders.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(o => EF.Functions.ILike(o.FullName, term) || EF.Functions.ILike(o.Phone, term));
            }

            var counts = await query.GroupBy(o => o.Status)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var thisMonth = await db.BookOrders.CountAsync(o => o.CreatedAt >= LocalTime.StartOfMonth(clock.GetUtcNow()), ct);

            if (status is { } s)
            {
                query = query.Where(o => o.Status == s);
            }

            var (number, size) = Paging(page, pageSize);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(o => o.CreatedAt)
                .Skip((number - 1) * size).Take(size)
                .Select(o => new BookOrderRow(o.Id, o.CreatedAt, o.FullName, o.Phone, o.Book.Slug, o.Book.Title,
                    o.DeliveryMethod, o.Address, o.Status))
                .ToListAsync(ct);

            var pending = await mail.PendingAsync(SubmissionKind.BookOrder, [.. items.Select(i => i.Id)], ct);
            items = [.. items.Select(i => i with { EmailPending = pending.Contains(i.Id) })];

            return Results.Ok(new SubmissionList<BookOrderRow, OrderStatus>(
                items, total, number, size, WithZeros(counts), thisMonth));
        })
            .WithSummary("Book orders, newest first; filter by status, search name or phone.");

        admin.MapGet("/book-orders/{id:int}", async (int id, SperantaDbContext db, CancellationToken ct) =>
            await OrderDetail(db, id, ct) is { } d ? Results.Ok(d) : Results.NotFound());

        admin.MapPatch("/book-orders/{id:int}", async (
            int id, [FromBody] UpdateBookOrderRequest req, SperantaDbContext db, UserManager<AppUser> users,
            ClaimsPrincipal principal, TimeProvider clock, CancellationToken ct) =>
        {
            var order = await db.BookOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order is null)
            {
                return Results.NotFound();
            }

            var v = new Validation();
            var actor = await Actor(users, principal);
            var now = clock.GetUtcNow();

            if (req.DeliveryMethod is { } method && method != order.DeliveryMethod)
            {
                order.DeliveryMethod = method;
                Log(db, SubmissionKind.BookOrder, id, $"Primire: {Labels.Of(method)}", actor, now);
            }

            if (req.Address is not null)
            {
                order.Address = v.Text("address", req.Address, Limits.Address, false, "");
            }

            if (order.DeliveryMethod == DeliveryMethod.Delivery && order.Address is null)
            {
                v.Add("address", "Pentru livrare e nevoie de adresă.");
            }

            if (req.InternalNote is not null)
            {
                order.InternalNote = v.Text("internalNote", req.InternalNote, Limits.Note, false, "");
            }

            if (req.Status is { } status && status != order.Status)
            {
                order.Status = status;
                Log(db, SubmissionKind.BookOrder, id, $"Status: {Labels.Of(status)}", actor, now);
            }

            if (!v.IsValid)
            {
                return v.Problem();
            }

            order.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await OrderDetail(db, id, ct));
        })
            .WithSummary("Change an order's status, delivery method, address or internal note.");

        // ---- Kids' sign-ups ------------------------------------------------------
        admin.MapGet("/kids-signups", async (
            SperantaDbContext db, MailHealth mail, TimeProvider clock, SignupStatus? status, string? q, int? page,
            int? pageSize, CancellationToken ct) =>
        {
            var query = db.KidsSignups.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(s => EF.Functions.ILike(s.ParentName, term)
                    || EF.Functions.ILike(s.Phone, term)
                    || (s.ChildFirstName != null && EF.Functions.ILike(s.ChildFirstName, term)));
            }

            var counts = await query.GroupBy(s => s.Status)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var thisMonth = await db.KidsSignups.CountAsync(s => s.CreatedAt >= LocalTime.StartOfMonth(clock.GetUtcNow()), ct);

            if (status is { } st)
            {
                query = query.Where(s => s.Status == st);
            }

            var (number, size) = Paging(page, pageSize);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(s => s.CreatedAt)
                .Skip((number - 1) * size).Take(size).ToListAsync(ct);

            var items = rows.Select(s => new KidsSignupRow(s.Id, s.CreatedAt, s.ParentName, s.Phone, s.Email,
                s.ChildFirstName, s.ChildAge, KidsAge.GroupFor(s.ChildAge), s.ParticipatedBefore, s.Message,
                s.Status)).ToList();

            var pending = await mail.PendingAsync(SubmissionKind.KidsSignup, [.. items.Select(i => i.Id)], ct);
            items = [.. items.Select(i => i with { EmailPending = pending.Contains(i.Id) })];

            return Results.Ok(new SubmissionList<KidsSignupRow, SignupStatus>(
                items, total, number, size, WithZeros(counts), thisMonth));
        })
            .WithSummary("Kids' sign-ups, newest first.");

        admin.MapGet("/kids-signups/{id:int}", async (int id, SperantaDbContext db, CancellationToken ct) =>
            await SignupDetail(db, id, ct) is { } d ? Results.Ok(d) : Results.NotFound());

        admin.MapPatch("/kids-signups/{id:int}", async (
            int id, [FromBody] UpdateKidsSignupRequest req, SperantaDbContext db, UserManager<AppUser> users,
            ClaimsPrincipal principal, TimeProvider clock, CancellationToken ct) =>
        {
            var signup = await db.KidsSignups.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (signup is null)
            {
                return Results.NotFound();
            }

            var v = new Validation();
            var now = clock.GetUtcNow();

            if (req.InternalNote is not null)
            {
                signup.InternalNote = v.Text("internalNote", req.InternalNote, Limits.Note, false, "");
            }

            if (req.Status is { } status && status != signup.Status)
            {
                signup.Status = status;
                Log(db, SubmissionKind.KidsSignup, id, $"Status: {Labels.Of(status)}", await Actor(users, principal), now);
            }

            if (!v.IsValid)
            {
                return v.Problem();
            }

            signup.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await SignupDetail(db, id, ct));
        });

        // ---- Contact messages ----------------------------------------------------
        admin.MapGet("/messages", async (
            SperantaDbContext db, MailHealth mail, TimeProvider clock, MessageStatus? status, string? q, int? page,
            int? pageSize, CancellationToken ct) =>
        {
            var query = db.ContactMessages.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = $"%{q.Trim()}%";
                query = query.Where(m => EF.Functions.ILike(m.Email, term)
                    || (m.FirstName != null && EF.Functions.ILike(m.FirstName, term))
                    || (m.LastName != null && EF.Functions.ILike(m.LastName, term))
                    || (m.Subject != null && EF.Functions.ILike(m.Subject, term)));
            }

            var counts = await query.GroupBy(m => m.Status)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var thisMonth = await db.ContactMessages.CountAsync(m => m.CreatedAt >= LocalTime.StartOfMonth(clock.GetUtcNow()), ct);

            if (status is { } st)
            {
                query = query.Where(m => m.Status == st);
            }

            var (number, size) = Paging(page, pageSize);
            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(m => m.CreatedAt)
                .Skip((number - 1) * size).Take(size).ToListAsync(ct);

            var items = rows.Select(m => new ContactMessageRow(m.Id, m.CreatedAt,
                Name(m.FirstName, m.LastName, m.Email), m.Email, m.Subject,
                m.Body.Length > 140 ? m.Body[..140] + "…" : m.Body, m.Status)).ToList();

            var pending = await mail.PendingAsync(SubmissionKind.ContactMessage, [.. items.Select(i => i.Id)], ct);
            items = [.. items.Select(i => i with { EmailPending = pending.Contains(i.Id) })];

            return Results.Ok(new SubmissionList<ContactMessageRow, MessageStatus>(
                items, total, number, size, WithZeros(counts), thisMonth));
        })
            .WithSummary("Contact messages, newest first.");

        admin.MapGet("/messages/{id:int}", async (int id, SperantaDbContext db, CancellationToken ct) =>
            await MessageDetail(db, id, ct) is { } d ? Results.Ok(d) : Results.NotFound());

        admin.MapPatch("/messages/{id:int}", async (
            int id, [FromBody] UpdateContactMessageRequest req, SperantaDbContext db, UserManager<AppUser> users,
            ClaimsPrincipal principal, TimeProvider clock, CancellationToken ct) =>
        {
            var message = await db.ContactMessages.FirstOrDefaultAsync(m => m.Id == id, ct);
            if (message is null)
            {
                return Results.NotFound();
            }

            if (req.Status != message.Status)
            {
                var now = clock.GetUtcNow();
                message.Status = req.Status;
                message.UpdatedAt = now;
                Log(db, SubmissionKind.ContactMessage, id, $"Status: {Labels.Of(req.Status)}",
                    await Actor(users, principal), now);
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(await MessageDetail(db, id, ct));
        });

        return admin;
    }

    private static (int Number, int Size) Paging(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? 25, 1, 100));

    /// <summary>Every status appears in the counts, so a tab with nothing in it reads 0, not missing.</summary>
    private static Dictionary<T, int> WithZeros<T>(Dictionary<T, int> counts) where T : struct, Enum =>
        Enum.GetValues<T>().ToDictionary(s => s, s => counts.GetValueOrDefault(s));

    private static string Child(string? name, int? age) => (name, age) switch
    {
        ({ } n, { } a) => $"{n}, {a} ani",
        ({ } n, null) => n,
        (null, { } a) => $"copil, {a} ani",
        _ => "copil",
    };

    private static string Name(string? first, string? last, string email)
    {
        var name = string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return name.Length == 0 ? email : name;
    }

    private static async Task<AppUser?> Actor(UserManager<AppUser> users, ClaimsPrincipal principal) =>
        await users.GetUserAsync(principal);

    private static void Log(
        SperantaDbContext db, SubmissionKind kind, int id, string text, AppUser? actor, DateTimeOffset at) =>
        db.Activity.Add(new ActivityEntry
        {
            Kind = kind,
            SubmissionId = id,
            Text = actor is null ? text : $"{text} · {actor.DisplayName ?? actor.Email}",
            ActorUserId = actor?.Id,
            At = at,
        });

    private static Task<List<ActivityDto>> History(SperantaDbContext db, SubmissionKind kind, int id, CancellationToken ct) =>
        db.Activity.AsNoTracking().Where(a => a.Kind == kind && a.SubmissionId == id)
            .OrderBy(a => a.At).ThenBy(a => a.Id)
            .Select(a => new ActivityDto(a.At, a.Text)).ToListAsync(ct);

    private static Task<bool> EmailPending(SperantaDbContext db, SubmissionKind kind, int id, CancellationToken ct) =>
        db.Outbox.AnyAsync(m => m.Kind == kind && m.SubmissionId == id && m.SentAt == null, ct);

    private static async Task<BookOrderDetail?> OrderDetail(SperantaDbContext db, int id, CancellationToken ct)
    {
        var o = await db.BookOrders.AsNoTracking().Include(x => x.Book).FirstOrDefaultAsync(x => x.Id == id, ct);
        return o is null
            ? null
            : new BookOrderDetail(o.Id, o.CreatedAt, o.UpdatedAt, o.FullName, o.Phone, o.Book.Slug, o.Book.Title,
                o.DeliveryMethod, o.Address, o.AgeGroup, o.HasBibleAtHome, o.Status, o.InternalNote,
                await History(db, SubmissionKind.BookOrder, id, ct),
                await EmailPending(db, SubmissionKind.BookOrder, id, ct));
    }

    private static async Task<KidsSignupDetail?> SignupDetail(SperantaDbContext db, int id, CancellationToken ct)
    {
        var s = await db.KidsSignups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null
            ? null
            : new KidsSignupDetail(s.Id, s.CreatedAt, s.UpdatedAt, s.ParentName, s.Phone, s.Email, s.ChildFirstName,
                s.ChildAge, KidsAge.GroupFor(s.ChildAge), s.ParticipatedBefore, s.Message, s.Status, s.InternalNote,
                await History(db, SubmissionKind.KidsSignup, id, ct),
                await EmailPending(db, SubmissionKind.KidsSignup, id, ct));
    }

    private static async Task<ContactMessageDetail?> MessageDetail(SperantaDbContext db, int id, CancellationToken ct)
    {
        var m = await db.ContactMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return m is null
            ? null
            : new ContactMessageDetail(m.Id, m.CreatedAt, m.FirstName, m.LastName, m.Email, m.Subject, m.Body,
                m.Status, await History(db, SubmissionKind.ContactMessage, id, ct),
                await EmailPending(db, SubmissionKind.ContactMessage, id, ct));
    }
}
