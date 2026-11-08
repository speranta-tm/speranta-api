using System.Net.Http.Json;
using System.Text.Json;

namespace Speranta.Api.Tests;

/// <summary>
/// The old site's real problem: its Gmail connection silently died every few days
/// and the notifications from those days were lost. These pin down that here a
/// refused mailbox delays notifications, is visible in the admin, and never loses one.
/// </summary>
public sealed class MailTests(AppFixture fixture) : AppTest(fixture)
{
    private async Task<int> OrderAsync()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/contact", new
        {
            email = "george@exemplu.ro",
            message = "Bună ziua!",
            acceptsDataUse = true,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reference").GetInt32();
    }

    [Fact]
    public async Task A_sent_notification_lands_in_the_history_only_once_it_really_went_out()
    {
        var id = await OrderAsync();
        var admin = await App.AdminClientAsync();

        var before = await admin.GetFromJsonAsync<JsonElement>($"/admin/messages/{id}");
        Assert.True(before.GetProperty("emailPending").GetBoolean());
        Assert.Equal(1, before.GetProperty("history").GetArrayLength());

        Assert.Equal(1, await App.DeliverMailAsync());

        var after = await admin.GetFromJsonAsync<JsonElement>($"/admin/messages/{id}");
        Assert.False(after.GetProperty("emailPending").GetBoolean());
        Assert.Equal("Email trimis la speranta.azs.tm@gmail.com",
            after.GetProperty("history")[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_refused_mailbox_shows_in_the_admin_and_nothing_is_lost()
    {
        App.Mail.FailWith = "535: 5.7.8 Username and Password not accepted.";
        var first = await OrderAsync();
        await OrderAsync();

        Assert.Equal(0, await App.DeliverMailAsync());

        var admin = await App.AdminClientAsync();
        var status = await admin.GetFromJsonAsync<JsonElement>("/admin/mail-status");
        Assert.Equal("Failing", status.GetProperty("state").GetString());
        Assert.Equal(2, status.GetProperty("pending").GetInt32());
        Assert.Contains("Password not accepted", status.GetProperty("lastError").GetString());
        Assert.Equal(App.Clock.GetUtcNow(), status.GetProperty("failingSince").GetDateTimeOffset());

        var list = await admin.GetFromJsonAsync<JsonElement>("/admin/messages");
        Assert.All(list.GetProperty("items").EnumerateArray(), i => Assert.True(i.GetProperty("emailPending").GetBoolean()));

        // Days later someone fixes the password; the retries deliver everything missed.
        App.Clock.Advance(TimeSpan.FromDays(3));
        App.Mail.FailWith = null;
        Assert.Equal(2, await App.DeliverMailAsync());
        admin = await App.SignInAsync("admin@speranta.test"); // the old token expired meanwhile

        var fixedStatus = await admin.GetFromJsonAsync<JsonElement>("/admin/mail-status");
        Assert.Equal("Ok", fixedStatus.GetProperty("state").GetString());
        Assert.Equal(0, fixedStatus.GetProperty("pending").GetInt32());
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/admin/messages/{first}");
        Assert.False(detail.GetProperty("emailPending").GetBoolean());
    }

    [Fact]
    public async Task Retries_back_off_instead_of_hammering_the_mailbox()
    {
        App.Mail.FailWith = "timeout";
        await OrderAsync();

        Assert.Equal(0, await App.DeliverMailAsync());
        App.Mail.FailWith = null;

        // Still inside the first back-off minute: nothing is attempted.
        App.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(0, await App.DeliverMailAsync());
        Assert.Empty(App.Mail.Delivered);

        App.Clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(1, await App.DeliverMailAsync());
    }

    [Fact]
    public async Task The_sign_in_check_catches_a_dead_password_on_a_quiet_day()
    {
        var admin = await App.AdminClientAsync();
        App.Mail.FailWith = "535: Username and Password not accepted.";

        var status = await (await admin.PostAsync("/admin/mail-status/check", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Failing", status.GetProperty("state").GetString());
        Assert.Equal(0, status.GetProperty("pending").GetInt32());

        App.Mail.FailWith = null;
        var again = await (await admin.PostAsync("/admin/mail-status/check", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ok", again.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Check_now_sends_what_was_waiting_without_waiting_for_the_next_retry()
    {
        App.Mail.FailWith = "timeout";
        await OrderAsync();
        await App.DeliverMailAsync();
        App.Mail.FailWith = null;

        var admin = await App.AdminClientAsync();
        var status = await (await admin.PostAsync("/admin/mail-status/check", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Ok", status.GetProperty("state").GetString());
        Assert.Single(App.Mail.Delivered);
    }

    [Fact]
    public async Task Without_SMTP_settings_mail_waits_and_the_admin_is_told_why()
    {
        App.Mail.IsConfigured = false;
        await OrderAsync();
        Assert.Equal(0, await App.DeliverMailAsync());

        var admin = await App.AdminClientAsync();
        var status = await admin.GetFromJsonAsync<JsonElement>("/admin/mail-status");
        Assert.Equal("NotConfigured", status.GetProperty("state").GetString());
        Assert.Equal(1, status.GetProperty("pending").GetInt32());
    }

    [Fact]
    public async Task The_test_email_goes_to_the_notification_address()
    {
        var admin = await App.AdminClientAsync();
        (await admin.PostAsync("/admin/mail-status/test", null)).EnsureSuccessStatusCode();

        var mail = Assert.Single(App.Mail.Delivered);
        Assert.Equal("speranta.azs.tm@gmail.com", mail.To);
        Assert.StartsWith("Test", mail.Subject);
    }

    [Fact]
    public void Back_off_grows_then_settles_at_an_hour()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), Email.MailDelivery.Backoff(1));
        Assert.Equal(TimeSpan.FromMinutes(15), Email.MailDelivery.Backoff(3));
        Assert.Equal(TimeSpan.FromHours(1), Email.MailDelivery.Backoff(20));
    }
}
