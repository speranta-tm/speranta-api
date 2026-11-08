using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Speranta.Api.Email;
using Speranta.Api.Push;
using Speranta.Infrastructure;
using Speranta.Infrastructure.Identity;

namespace Speranta.Api.Tests;

/// <summary>
/// Hosts the real app against a real Postgres. Not an in-memory provider: ILIKE
/// search, check constraints, the seeded sequences and ExecuteUpdate are all
/// Postgres behaviour a fake would quietly skip.
/// </summary>
public sealed class SperantaAppFactory : WebApplicationFactory<Program>
{
    private const string TestDatabase = "speranta_test";
    public const string Password = "correct-horse-battery";

    /// <summary>Stands in for Gmail: records what it was asked to send, and can be told to refuse.</summary>
    public FakeMailTransport Mail { get; } = new();

    /// <summary>Phone alerts the app asked to send.</summary>
    public RecordingPush Push { get; } = new();

    public TestClock Clock { get; } = new(DateTimeOffset.Parse("2026-11-08T09:00:00Z"));

    /// <summary>Extra settings for a one-off factory, e.g. a tiny rate limit.</summary>
    public Dictionary<string, string?> Overrides { get; } = [];

    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "speranta-test-files", Guid.NewGuid().ToString("N"));

    private static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("SPERANTA_TEST_POSTGRES")
        ?? "Host=localhost;Port=5434;Database=postgres;Username=speranta;Password=speranta_dev";

    private static string TestConnectionString =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = TestDatabase }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting, not ConfigureAppConfiguration: Program reads configuration while
        // the builder is constructed, before those delegates run.
        builder.UseSetting("ConnectionStrings:Postgres", TestConnectionString);
        builder.UseSetting("RateLimits:GlobalPermitsPerMinute", "1000000");
        builder.UseSetting("RateLimits:AuthPermitsPerMinute", "1000000");
        builder.UseSetting("RateLimits:FormPermitsPerTenMinutes", "1000000");
        builder.UseSetting("Email:WebUrl", "https://site.test");
        builder.UseSetting("Storage:Path", StoragePath);

        foreach (var (key, value) in Overrides)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMailTransport>();
            services.AddSingleton<IMailTransport>(Mail);

            // No background sender in tests: they run a delivery pass when they
            // want one (DeliverMailAsync), so nothing depends on timing.
            var sender = services.Single(d => d.ImplementationType == typeof(MailSenderService));
            services.Remove(sender);

            services.RemoveAll<IPushNotifier>();
            services.AddSingleton<IPushNotifier>(Push);
            services.Remove(services.Single(d => d.ImplementationType == typeof(PushSenderService)));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Creates and migrates the test database without booting the host
    /// (whose startup tasks need the schema to exist already).</summary>
    public static async Task EnsureDatabaseAsync()
    {
        await using (var connection = new NpgsqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync();
            await using var exists = new NpgsqlCommand("select 1 from pg_database where datname = @n", connection);
            exists.Parameters.AddWithValue("n", TestDatabase);

            if (await exists.ExecuteScalarAsync() is null)
            {
                // The name is a constant; CREATE DATABASE cannot take parameters.
                await using var create = new NpgsqlCommand($"create database \"{TestDatabase}\"", connection);
                await create.ExecuteNonQueryAsync();
            }
        }

        var options = new DbContextOptionsBuilder<SperantaDbContext>()
            .UseNpgsql(TestConnectionString).UseSnakeCaseNamingConvention().Options;
        await using var db = new SperantaDbContext(options);
        await db.Database.MigrateAsync();
    }

    /// <summary>Empties what tests write and puts the seeded content back as it ships.</summary>
    public async Task ResetAsync()
    {
        Mail.Reset();
        Push.Sent.Clear();
        Services.GetRequiredService<MailCredentials>().UseSaved(null);
        Clock.Set(DateTimeOffset.Parse("2026-11-08T09:00:00Z"));

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SperantaDbContext>();

        await db.Database.ExecuteSqlRawAsync(
            "truncate book_orders, kids_signups, contact_messages, activity, announcements, admin_invites, outbox_emails, push_subscriptions, "
            + "users, user_roles, user_logins, user_claims, user_tokens restart identity cascade;"
            + "update books set is_available = true, sort_order = id;"
            + "update site_settings set flyer_file_id = null, flyer_downloads = 0, kids_phone = '0764 915 396', smtp_password_protected = null;"
            + "delete from files;"
            + "update mail_status set last_success_at = null, last_failure_at = null, last_error = null, "
            + "last_check_at = null, last_check_ok = null, last_alert_at = null;");
    }

    /// <summary>An admin account, signed in, as a bearer client.</summary>
    public async Task<HttpClient> AdminClientAsync(string email = "admin@speranta.test", string? name = "Marc")
    {
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name };
            Check(await users.CreateAsync(user, Password));
            Check(await users.AddToRoleAsync(user, AppRoles.Admin));
        }

        return await SignInAsync(email);
    }

    public async Task<HttpClient> SignInAsync(string email)
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();

        var token = (await login.Content.ReadFromJsonAsync<TokenPayload>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    /// <summary>Everything queued in the outbox so far, sent or not, oldest first.</summary>
    public List<OutgoingMail> Outbox() => Db(db => db.Outbox.OrderBy(m => m.Id).ToList()
        .Select(m => new OutgoingMail(m.To, m.Subject, m.HtmlBody,
            m.Kind is { } k && m.SubmissionId is { } id ? (k, id) : null))
        .ToList());

    /// <summary>One pass of the background sender.</summary>
    public async Task<int> DeliverMailAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MailDelivery>().ProcessDueAsync(CancellationToken.None);
    }

    public T Db<T>(Func<SperantaDbContext, T> read)
    {
        using var scope = Services.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<SperantaDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private sealed record TokenPayload(string AccessToken);
}

public sealed class RecordingPush : IPushNotifier
{
    public List<PushMessage> Sent { get; } = [];

    public void Notify(PushMessage message)
    {
        lock (Sent)
        {
            Sent.Add(message);
        }
    }
}

public sealed class FakeMailTransport : IMailTransport
{
    public List<OutgoingMail> Delivered { get; } = [];

    /// <summary>Set to make every send and sign-in fail, like an expired Gmail password.</summary>
    public string? FailWith { get; set; }

    public bool IsConfigured { get; set; } = true;

    public void Reset()
    {
        Delivered.Clear();
        FailWith = null;
        IsConfigured = true;
    }

    public Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        if (FailWith is not null)
        {
            throw new InvalidOperationException(FailWith);
        }

        Delivered.Add(mail);
        return Task.CompletedTask;
    }

    public Task CheckAsync(CancellationToken ct) =>
        FailWith is null ? Task.CompletedTask : throw new InvalidOperationException(FailWith);
}

public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Set(DateTimeOffset now) => _now = now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>One app + database for the whole run; tests in it run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
    public const string Name = "app";
}

public sealed class AppFixture : IAsyncLifetime
{
    public SperantaAppFactory App { get; } = new();

    public async Task InitializeAsync() => await SperantaAppFactory.EnsureDatabaseAsync();

    public async Task DisposeAsync() => await App.DisposeAsync();
}

/// <summary>Base for tests sharing the app: resets the data before each test.</summary>
[Collection(AppCollection.Name)]
public abstract class AppTest(AppFixture fixture) : IAsyncLifetime
{
    protected SperantaAppFactory App => fixture.App;

    public Task InitializeAsync() => App.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}
