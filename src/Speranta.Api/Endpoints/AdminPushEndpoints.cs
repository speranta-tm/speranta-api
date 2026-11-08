using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Speranta.Api.Push;
using Speranta.Infrastructure;

namespace Speranta.Api.Endpoints;

public sealed record PushKeys(string P256dh, string Auth);

/// <summary>The shape the browser's PushSubscription.toJSON() produces.</summary>
public sealed record PushSubscribeRequest(string? Endpoint, PushKeys? Keys);

public static class AdminPushEndpoints
{
    public static RouteGroupBuilder MapAdminPush(this RouteGroupBuilder admin)
    {
        admin.MapGet("/push/key", (VapidKeys keys) =>
            keys.IsReady ? Results.Ok(new { publicKey = keys.PublicKey }) : Results.NoContent())
            .WithSummary("The public key a browser needs to subscribe to alerts.");

        admin.MapPost("/push/subscriptions", async (
            [FromBody] PushSubscribeRequest req, SperantaDbContext db, ClaimsPrincipal principal,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Endpoint) || !req.Endpoint.StartsWith("https://", StringComparison.Ordinal)
                || req.Keys is null || string.IsNullOrWhiteSpace(req.Keys.P256dh) || string.IsNullOrWhiteSpace(req.Keys.Auth))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["endpoint"] = ["Abonamentul de notificări nu este valid."],
                });
            }

            var userId = principal.Id();
            var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);
            if (sub is null)
            {
                db.PushSubscriptions.Add(new Domain.Entities.PushSubscription
                {
                    UserId = userId,
                    Endpoint = req.Endpoint,
                    P256dh = req.Keys.P256dh,
                    Auth = req.Keys.Auth,
                    CreatedAt = clock.GetUtcNow(),
                });
            }
            else
            {
                // Same device, maybe another admin signed in on it now.
                sub.UserId = userId;
                sub.P256dh = req.Keys.P256dh;
                sub.Auth = req.Keys.Auth;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
            .WithSummary("Turn alerts on for this browser or phone.");

        admin.MapDelete("/push/subscriptions", async (
            string endpoint, SperantaDbContext db, CancellationToken ct) =>
        {
            await db.PushSubscriptions.Where(s => s.Endpoint == endpoint).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        })
            .WithSummary("Turn alerts off for this browser or phone.");

        admin.MapPost("/push/test", (IPushNotifier push, ClaimsPrincipal principal) =>
        {
            push.Notify(new PushMessage("Alertele funcționează", "Așa vei afla de comenzi, înscrieri și mesaje noi.",
                "/admin", principal.Id()));
            return Results.NoContent();
        })
            .WithSummary("Send a test alert to the signed-in admin's devices.");

        return admin;
    }
}
