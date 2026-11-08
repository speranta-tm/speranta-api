using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Speranta.Api.Email;
using Speranta.Infrastructure;
using WebPush;

namespace Speranta.Api.Push;

/// <summary>
/// A phone alert. <see cref="UserId"/> null means every admin who turned alerts on.
/// <see cref="Url"/> is the admin page that opens when it is tapped.
/// </summary>
public sealed record PushMessage(string Title, string Body, string Url, Guid? UserId = null);

/// <summary>
/// Sends alerts to admins' phones and browsers (Web Push). Deliberately independent
/// of Gmail: when email is what broke, this is how someone still hears about it.
/// </summary>
public interface IPushNotifier
{
    void Notify(PushMessage message);
}

/// <summary>Queues alerts for <see cref="PushSenderService"/>. Losing one on a restart is acceptable; email and the panel still have everything.</summary>
public sealed class ChannelPushNotifier : IPushNotifier
{
    private readonly Channel<PushMessage> _channel =
        Channel.CreateBounded<PushMessage>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<PushMessage> Reader => _channel.Reader;

    public void Notify(PushMessage message) => _channel.Writer.TryWrite(message);
}

/// <summary>The VAPID key pair that identifies this site to the browsers' push services.</summary>
public sealed class VapidKeys
{
    public string PublicKey { get; private set; } = "";

    public string PrivateKey { get; private set; } = "";

    public bool IsReady => PublicKey.Length > 0;

    private const string Purpose = "Speranta.Vapid";

    /// <summary>Loads the keys, or makes and stores them on the very first start.</summary>
    public static async Task LoadAsync(IServiceProvider services, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SperantaDbContext>();
        var keys = scope.ServiceProvider.GetRequiredService<VapidKeys>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
        var settings = await db.Settings.FirstAsync();

        if (settings.VapidPublicKey is { } pub && settings.VapidPrivateKeyProtected is { } priv)
        {
            try
            {
                keys.PublicKey = pub;
                keys.PrivateKey = protector.Unprotect(priv);
                return;
            }
            catch (Exception ex)
            {
                // Unreadable after a key-folder loss: make new keys. Phones that had
                // turned alerts on must turn them on again.
                logger.LogWarning(ex, "VAPID private key unreadable; generating a new pair.");
                await db.PushSubscriptions.ExecuteDeleteAsync();
            }
        }

        var pair = VapidHelper.GenerateVapidKeys();
        settings.VapidPublicKey = pair.PublicKey;
        settings.VapidPrivateKeyProtected = protector.Protect(pair.PrivateKey);
        await db.SaveChangesAsync();
        keys.PublicKey = pair.PublicKey;
        keys.PrivateKey = pair.PrivateKey;
    }
}

public sealed class PushSenderService(
    ChannelPushNotifier queue, VapidKeys keys, IServiceScopeFactory scopes, IOptions<EmailOptions> email,
    ILogger<PushSenderService> logger) : BackgroundService
{
    private readonly WebPushClient _client = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!keys.IsReady)
            {
                continue;
            }

            try
            {
                await SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Push alert '{Title}' failed.", message.Title);
            }
        }
    }

    private async Task SendAsync(PushMessage message, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SperantaDbContext>();
        var subs = await db.PushSubscriptions
            .Where(s => message.UserId == null || s.UserId == message.UserId)
            .ToListAsync(ct);

        var vapid = new VapidDetails($"mailto:{email.Value.NotifyTo}", keys.PublicKey, keys.PrivateKey);
        var payload = JsonSerializer.Serialize(new { title = message.Title, body = message.Body, url = message.Url });

        foreach (var sub in subs)
        {
            try
            {
                await _client.SendNotificationAsync(
                    new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth), payload, vapid, ct);
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                // The browser threw the subscription away (alerts turned off, app removed).
                db.PushSubscriptions.Remove(sub);
            }
            catch (WebPushException ex)
            {
                logger.LogWarning(ex, "Push to one device failed ({Status}).", ex.StatusCode);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
