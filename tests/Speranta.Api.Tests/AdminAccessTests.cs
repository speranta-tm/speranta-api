using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Speranta.Api.Setup;

namespace Speranta.Api.Tests;

public sealed partial class AdminAccessTests(AppFixture fixture) : AppTest(fixture)
{
    [Fact]
    public async Task There_is_no_public_sign_up()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "x@speranta.test", password = SperantaAppFactory.Password });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var malformed = await App.CreateClient().PostAsJsonAsync("/auth/register", new { });
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
    }

    [Theory]
    [InlineData("/admin/dashboard")]
    [InlineData("/admin/book-orders")]
    [InlineData("/admin/admins")]
    public async Task The_admin_is_closed_to_anonymous_visitors(string path)
    {
        var response = await App.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_invitation_becomes_an_admin_who_can_sign_in()
    {
        var marc = await App.AdminClientAsync();
        (await marc.PostAsJsonAsync("/admin/admins/invites", new { email = "voluntar@speranta.test" }))
            .EnsureSuccessStatusCode();

        var token = TokenFrom(Assert.Single(App.Outbox()).HtmlBody);
        var anon = App.CreateClient();

        var info = await anon.GetFromJsonAsync<JsonElement>($"/invites/{token}");
        Assert.Equal("voluntar@speranta.test", info.GetProperty("email").GetString());
        Assert.Equal("Marc", info.GetProperty("invitedBy").GetString());

        var tooShort = await anon.PostAsJsonAsync($"/invites/{token}/accept", new { password = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Contains("cel puțin 4 caractere", await tooShort.Content.ReadAsStringAsync());

        (await anon.PostAsJsonAsync($"/invites/{token}/accept",
            new { password = SperantaAppFactory.Password, displayName = "Voluntar" })).EnsureSuccessStatusCode();

        var volunteer = await App.SignInAsync("voluntar@speranta.test");
        var me = await volunteer.GetFromJsonAsync<JsonElement>("/admin/me");
        Assert.Equal("Voluntar", me.GetProperty("displayName").GetString());

        // Used once, then gone.
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/invites/{token}")).StatusCode);
    }

    [Fact]
    public async Task Four_characters_of_any_kind_are_a_valid_password()
    {
        var marc = await App.AdminClientAsync();
        (await marc.PostAsJsonAsync("/admin/admins/invites", new { email = "short@speranta.test" }))
            .EnsureSuccessStatusCode();
        var token = TokenFrom(Assert.Single(App.Outbox()).HtmlBody);
        var anon = App.CreateClient();

        // Digits only: no lowercase, uppercase or symbol is required.
        (await anon.PostAsJsonAsync($"/invites/{token}/accept", new { password = "1234" }))
            .EnsureSuccessStatusCode();

        var login = await anon.PostAsJsonAsync("/auth/login", new { email = "short@speranta.test", password = "1234" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task An_invitation_expires_after_seven_days()
    {
        var marc = await App.AdminClientAsync();
        await marc.PostAsJsonAsync("/admin/admins/invites", new { email = "late@speranta.test" });
        var token = TokenFrom(Assert.Single(App.Outbox()).HtmlBody);

        App.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));

        var response = await App.CreateClient().PostAsJsonAsync($"/invites/{token}/accept",
            new { password = SperantaAppFactory.Password });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_second_invitation_replaces_the_first()
    {
        var marc = await App.AdminClientAsync();
        await marc.PostAsJsonAsync("/admin/admins/invites", new { email = "twice@speranta.test" });
        await marc.PostAsJsonAsync("/admin/admins/invites", new { email = "twice@speranta.test" });

        var first = TokenFrom(App.Outbox()[0].HtmlBody);
        var second = TokenFrom(App.Outbox()[1].HtmlBody);
        var anon = App.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"/invites/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"/invites/{second}")).StatusCode);

        var list = await marc.GetFromJsonAsync<JsonElement>("/admin/admins");
        Assert.Equal(1, list.GetProperty("invites").GetArrayLength());
    }

    [Fact]
    public async Task A_removed_admin_is_locked_out_at_once()
    {
        var marc = await App.AdminClientAsync();
        var other = await App.AdminClientAsync("other@speranta.test", "Other");

        var list = await marc.GetFromJsonAsync<JsonElement>("/admin/admins");
        var otherId = list.GetProperty("admins").EnumerateArray()
            .Single(a => a.GetProperty("email").GetString() == "other@speranta.test").GetProperty("id").GetString();

        (await marc.DeleteAsync($"/admin/admins/{otherId}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/admin/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Nobody_can_remove_themselves()
    {
        var marc = await App.AdminClientAsync();
        var me = await marc.GetFromJsonAsync<JsonElement>("/admin/me");

        var response = await marc.DeleteAsync($"/admin/admins/{me.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_invites_an_address_with_no_account_once()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bootstrap:AdminEmails:0"] = "first@speranta.test" })
            .Build();

        await StartupTasks.RunAsync(App.Services, config, NullLogger.Instance);
        await StartupTasks.RunAsync(App.Services, config, NullLogger.Instance);

        var mail = Assert.Single(App.Outbox());
        Assert.Equal("first@speranta.test", mail.To);
    }

    private static string TokenFrom(string html) => Token().Match(html).Groups[1].Value;

    [GeneratedRegex("token=([A-Za-z0-9_-]+)")]
    private static partial Regex Token();
}
