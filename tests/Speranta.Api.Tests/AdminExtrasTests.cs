using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Speranta.Api.Email;

namespace Speranta.Api.Tests;

public sealed partial class AdminExtrasTests(AppFixture fixture) : AppTest(fixture)
{
    private async Task SendMessageAsync(string email = "george@exemplu.ro") =>
        (await App.CreateClient().PostAsJsonAsync("/forms/contact",
            new { email, message = "Bună ziua!", subject = "Program", acceptsDataUse = true })).EnsureSuccessStatusCode();

    // ---- Phone alerts -------------------------------------------------------------

    [Fact]
    public async Task Every_submission_alerts_the_admins_phones()
    {
        await SendMessageAsync();
        var alert = Assert.Single(App.Push.Sent);
        Assert.Equal("Mesaj nou de pe site", alert.Title);
        Assert.StartsWith("/admin/mesaje/", alert.Url);
        Assert.Null(alert.UserId);
    }

    [Fact]
    public async Task Failing_email_alerts_phones_once_then_daily_and_again_after_recovery()
    {
        App.Mail.FailWith = "535: Username and Password not accepted.";
        await SendMessageAsync();
        App.Push.Sent.Clear();

        await App.DeliverMailAsync();
        App.Clock.Advance(TimeSpan.FromHours(2));
        await App.DeliverMailAsync();
        Assert.Single(App.Push.Sent, p => p.Title.Contains("nu mai pleacă"));

        App.Clock.Advance(TimeSpan.FromHours(23));
        await App.DeliverMailAsync();
        Assert.Equal(2, App.Push.Sent.Count(p => p.Title.Contains("nu mai pleacă")));

        // Fixed: it works again, and the next breakage alerts at once.
        App.Mail.FailWith = null;
        App.Clock.Advance(TimeSpan.FromHours(1));
        await App.DeliverMailAsync();
        App.Mail.FailWith = "timeout";
        await SendMessageAsync();
        await App.DeliverMailAsync();
        Assert.Equal(3, App.Push.Sent.Count(p => p.Title.Contains("nu mai pleacă")));
    }

    [Fact]
    public async Task A_device_can_turn_alerts_on_and_off()
    {
        var admin = await App.AdminClientAsync();
        var key = await admin.GetFromJsonAsync<JsonElement>("/admin/push/key");
        Assert.False(string.IsNullOrEmpty(key.GetProperty("publicKey").GetString()));

        var sub = new { endpoint = "https://push.example/abc", keys = new { p256dh = "BKey", auth = "auth" } };
        (await admin.PostAsJsonAsync("/admin/push/subscriptions", sub)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/admin/push/subscriptions", sub)).EnsureSuccessStatusCode();
        Assert.Equal(1, App.Db(db => db.PushSubscriptions.Count()));

        var bad = await admin.PostAsJsonAsync("/admin/push/subscriptions", new { endpoint = "http://insecure", keys = sub.keys });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        (await admin.DeleteAsync("/admin/push/subscriptions?endpoint=" + Uri.EscapeDataString(sub.endpoint))).EnsureSuccessStatusCode();
        Assert.Equal(0, App.Db(db => db.PushSubscriptions.Count()));
    }

    // ---- The app password from the panel ------------------------------------------

    [Fact]
    public async Task An_admin_can_paste_a_new_app_password_and_waiting_mail_goes_out()
    {
        App.Mail.FailWith = "535";
        await SendMessageAsync();
        await App.DeliverMailAsync();
        App.Mail.FailWith = null; // as if the new password is the right one

        var admin = await App.AdminClientAsync();
        var response = await admin.PutAsJsonAsync("/admin/mail-settings", new { appPassword = "abcd efgh ijkl mnop" });
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ok", status.GetProperty("state").GetString());
        Assert.Single(App.Mail.Delivered);

        // In use at once, and stored only encrypted.
        Assert.Equal("abcdefghijklmnop", App.Services.GetRequiredService<MailCredentials>().Password);
        var stored = App.Db(db => db.Settings.Single().SmtpPasswordProtected);
        Assert.NotNull(stored);
        Assert.DoesNotContain("abcdefghijklmnop", stored);
    }

    [Fact]
    public async Task An_empty_app_password_is_refused()
    {
        var admin = await App.AdminClientAsync();
        var response = await admin.PutAsJsonAsync("/admin/mail-settings", new { appPassword = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Invitations with a name ---------------------------------------------------

    [Fact]
    public async Task The_name_typed_in_the_invitation_becomes_the_admins_name()
    {
        var marc = await App.AdminClientAsync();
        (await marc.PostAsJsonAsync("/admin/admins/invites", new { email = "daniel@speranta.test", displayName = "Daniel Pop" }))
            .EnsureSuccessStatusCode();

        var list = await marc.GetFromJsonAsync<JsonElement>("/admin/admins");
        Assert.Equal("Daniel Pop", list.GetProperty("invites")[0].GetProperty("displayName").GetString());

        var token = Token().Match(App.Outbox().Single().HtmlBody).Groups[1].Value;
        var info = await App.CreateClient().GetFromJsonAsync<JsonElement>($"/invites/{token}");
        Assert.Equal("Daniel Pop", info.GetProperty("displayName").GetString());

        (await App.CreateClient().PostAsJsonAsync($"/invites/{token}/accept", new { password = SperantaAppFactory.Password }))
            .EnsureSuccessStatusCode();
        var daniel = await App.SignInAsync("daniel@speranta.test");
        Assert.Equal("Daniel Pop", (await daniel.GetFromJsonAsync<JsonElement>("/admin/me")).GetProperty("displayName").GetString());
    }

    // ---- CSV ------------------------------------------------------------------------

    [Fact]
    public async Task Orders_export_as_a_csv_excel_opens_safely()
    {
        (await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = "biblia", fullName = "=HYPERLINK(\"http://evil\")", phone = "0712 345 678", address = "Str. Ș; nr. 1",
            ageGroup = "Over70", hasBibleAtHome = false, acceptsDeliveryArea = true, acceptsDataUse = true,
        })).EnsureSuccessStatusCode();

        var admin = await App.AdminClientAsync();
        var response = await admin.GetAsync("/admin/book-orders.csv");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("comenzi-carti-2026-11-08.csv", response.Content.Headers.ContentDisposition!.ToString());

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var lines = Encoding.UTF8.GetString(bytes[3..]).Split("\r\n");
        Assert.StartsWith("Nr.;Data;Nume;Telefon;Carte", lines[0]);
        Assert.Contains("\"'=HYPERLINK(\"\"http://evil\"\")\"", lines[1]);
        Assert.Contains("\"Str. Ș; nr. 1\"", lines[1]);
        Assert.Contains("Peste 70", lines[1]);
    }

    [Fact]
    public async Task Sign_ups_export_with_the_lists_filter()
    {
        foreach (var name in new[] { "Ana", "Bogdan" })
        {
            (await App.CreateClient().PostAsJsonAsync("/forms/kids-signups", new
            {
                parentName = name, phone = "0711222333", childAge = 6, participatedBefore = false, acceptsDataUse = true,
            })).EnsureSuccessStatusCode();
        }

        var admin = await App.AdminClientAsync();
        var csv = await admin.GetStringAsync("/admin/kids-signups.csv?q=bogdan");
        var lines = csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Bogdan", lines[1]);
        Assert.Contains("Până la 7 ani", lines[1]);
    }

    [GeneratedRegex("token=([A-Za-z0-9_-]+)")]
    private static partial Regex Token();
}
