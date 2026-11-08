using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Speranta.Api.Contracts;
using Speranta.Api.Email;
using Speranta.Api.Setup;
using Speranta.Domain;
using Speranta.Domain.Entities;
using Speranta.Infrastructure;

namespace Speranta.Api.Endpoints;

/// <summary>
/// The three public forms. Each one saves to the database first and then queues
/// the notification email, so a broken mailbox never loses a submission.
/// </summary>
public static class FormEndpoints
{
    public static IEndpointRouteBuilder MapForms(this IEndpointRouteBuilder app)
    {
        var forms = app.MapGroup("/forms").RequireRateLimiting(RateLimitSetup.FormPolicy);

        forms.MapPost("/book-orders", async (
            [FromBody] BookOrderRequest req, SperantaDbContext db, Notifications notify,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (IsBot(req.Website))
            {
                return Results.Ok(new SubmissionReceipt(0));
            }

            var v = new Validation();
            var name = v.Text("fullName", req.FullName, Limits.Name, true, "Scrieți numele complet.");
            var phone = v.Phone("phone", req.Phone, required: true);

            var book = string.IsNullOrWhiteSpace(req.BookSlug)
                ? null
                : await db.Books.FirstOrDefaultAsync(b => b.Slug == req.BookSlug, ct);

            if (book is null)
            {
                v.Add("bookSlug", "Cartea nu există.");
            }
            else if (!book.IsAvailable)
            {
                v.Add("bookSlug", "Ne pare rău, această carte nu mai este în stoc.");
            }

            var method = req.DeliveryMethod ?? DeliveryMethod.Delivery;
            if (!Enum.IsDefined(method) || (method == DeliveryMethod.Pickup && book is { AllowsPickup: false }))
            {
                v.Add("deliveryMethod", "Această carte se poate doar livra.");
            }

            // The address only matters when someone drives there.
            var address = method == DeliveryMethod.Delivery
                ? v.Text("address", req.Address, Limits.Address, true, "Scrieți adresa completă.")
                : null;

            if (book is { AsksAgeGroup: true } && (req.AgeGroup is null || !Enum.IsDefined(req.AgeGroup.Value)))
            {
                v.Add("ageGroup", "Alegeți grupa de vârstă.");
            }

            if (book is { AsksBibleAtHome: true } && req.HasBibleAtHome is null)
            {
                v.Add("hasBibleAtHome", "Alegeți un răspuns.");
            }

            v.MustAccept("acceptsDeliveryArea", req.AcceptsDeliveryArea,
                "Confirmați că locuiți în Timișoara sau împrejurimi.");
            v.MustAccept("acceptsDataUse", req.AcceptsDataUse,
                "Avem nevoie de acordul dvs. pentru a vă putea livra cartea.");

            if (!v.IsValid)
            {
                return v.Problem();
            }

            var now = clock.GetUtcNow();
            var order = new BookOrder
            {
                BookId = book!.Id,
                FullName = name!,
                Phone = phone!,
                DeliveryMethod = method,
                Address = address,
                // Only what this book's form asked is kept, so a crafted request can't
                // put an answer nobody was asked on the record.
                AgeGroup = book.AsksAgeGroup ? req.AgeGroup : null,
                HasBibleAtHome = book.AsksBibleAtHome ? req.HasBibleAtHome : null,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.BookOrders.Add(order);
            await db.SaveChangesAsync(ct);
            await Received(db, SubmissionKind.BookOrder, order.Id, "Comandă primită de pe site", now, ct);

            await notify.NewBookOrderAsync(order, book, ct);
            return Results.Ok(new SubmissionReceipt(order.Id));
        })
            .WithSummary("Order a free book (Timișoara and nearby only).");

        forms.MapPost("/kids-signups", async (
            [FromBody] KidsSignupRequest req, SperantaDbContext db, Notifications notify,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (IsBot(req.Website))
            {
                return Results.Ok(new SubmissionReceipt(0));
            }

            var v = new Validation();
            var parent = v.Text("parentName", req.ParentName, Limits.Name, true, "Scrieți numele dvs. complet.");
            var phone = v.Phone("phone", req.Phone, required: true);
            var email = v.Email("email", req.Email, required: false);
            var child = v.Text("childFirstName", req.ChildFirstName, Limits.Name, false, "");
            var message = v.Text("message", req.Message, Limits.Message, false, "");

            if (req.ChildAge is < 0 or > 18)
            {
                v.Add("childAge", "Vârsta trebuie să fie între 0 și 18 ani.");
            }

            if (req.ParticipatedBefore is null)
            {
                v.Add("participatedBefore", "Alegeți un răspuns.");
            }

            v.MustAccept("acceptsDataUse", req.AcceptsDataUse,
                "Avem nevoie de acordul dvs. pentru a vă contacta.");

            if (!v.IsValid)
            {
                return v.Problem();
            }

            var now = clock.GetUtcNow();
            var signup = new KidsSignup
            {
                ParentName = parent!,
                Phone = phone!,
                Email = email,
                ChildFirstName = child,
                ChildAge = req.ChildAge,
                ParticipatedBefore = req.ParticipatedBefore!.Value,
                Message = message,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.KidsSignups.Add(signup);
            await db.SaveChangesAsync(ct);
            await Received(db, SubmissionKind.KidsSignup, signup.Id, "Înscriere primită de pe site", now, ct);

            await notify.NewKidsSignupAsync(signup, ct);
            return Results.Ok(new SubmissionReceipt(signup.Id));
        })
            .WithSummary("Sign a child up for the Saturday Bible activities.");

        forms.MapPost("/contact", async (
            [FromBody] ContactMessageRequest req, SperantaDbContext db, Notifications notify,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (IsBot(req.Website))
            {
                return Results.Ok(new SubmissionReceipt(0));
            }

            var v = new Validation();
            var first = v.Text("firstName", req.FirstName, Limits.Name, false, "");
            var last = v.Text("lastName", req.LastName, Limits.Name, false, "");
            var email = v.Email("email", req.Email, required: true);
            var subject = v.Text("subject", req.Subject, Limits.Subject, false, "");
            var body = v.Text("message", req.Message, Limits.Message, true, "Scrieți mesajul.");
            v.MustAccept("acceptsDataUse", req.AcceptsDataUse,
                "Avem nevoie de acordul dvs. pentru a vă răspunde.");

            if (!v.IsValid)
            {
                return v.Problem();
            }

            var now = clock.GetUtcNow();
            var message = new ContactMessage
            {
                FirstName = first,
                LastName = last,
                Email = email!,
                Subject = subject,
                Body = body!,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.ContactMessages.Add(message);
            await db.SaveChangesAsync(ct);
            await Received(db, SubmissionKind.ContactMessage, message.Id, "Mesaj primit de pe site", now, ct);

            await notify.NewContactMessageAsync(message, ct);
            return Results.Ok(new SubmissionReceipt(message.Id));
        })
            .WithSummary("Send the church a message.");

        return app;
    }

    /// <summary>
    /// The hidden "website" field. A bot that fills it gets the same 200 a person
    /// does, so it has no signal to adapt to; nothing is saved or sent.
    /// </summary>
    private static bool IsBot(string? honeypot) => !string.IsNullOrEmpty(honeypot);

    private static async Task Received(
        SperantaDbContext db, SubmissionKind kind, int id, string text, DateTimeOffset at, CancellationToken ct)
    {
        db.Activity.Add(new ActivityEntry { Kind = kind, SubmissionId = id, Text = text, At = at });
        await db.SaveChangesAsync(ct);
    }
}
