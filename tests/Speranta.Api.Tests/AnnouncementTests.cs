using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Speranta.Api.Tests;

public sealed class AnnouncementTests(AppFixture fixture) : AppTest(fixture)
{
    private static object Save(string title, string status = "Published", string body = "<p>Text</p>",
        DateTimeOffset? publishAt = null, Guid? cover = null, bool home = true) => new
    {
        title, bodyHtml = body, status, publishAt, coverFileId = cover, showOnHome = home,
    };

    [Fact]
    public async Task The_body_keeps_formatting_but_loses_anything_executable()
    {
        var admin = await App.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/admin/announcements", Save("Program",
            body: "<h2>Titlu</h2><p onclick=\"x()\">Salut <strong>tuturor</strong> "
                + "<a href=\"javascript:alert(1)\">rău</a> <a href=\"https://adventist.ro\">bun</a></p>"
                + "<script>alert(1)</script><iframe src=\"https://evil.test\"></iframe><ul><li>unu</li></ul>"));
        created.EnsureSuccessStatusCode();

        var html = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("bodyHtml").GetString()!;
        Assert.Contains("<h2>Titlu</h2>", html);
        Assert.Contains("<strong>tuturor</strong>", html);
        Assert.Contains("<li>unu</li>", html);
        Assert.Contains("href=\"https://adventist.ro\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
        Assert.DoesNotContain("script", html);
        Assert.DoesNotContain("iframe", html);
        Assert.DoesNotContain("onclick", html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public async Task Drafts_and_scheduled_ones_stay_off_the_site_until_their_time()
    {
        var admin = await App.AdminClientAsync();
        await admin.PostAsJsonAsync("/admin/announcements", Save("Ciornă", status: "Draft"));
        await admin.PostAsJsonAsync("/admin/announcements",
            Save("Mai târziu", publishAt: App.Clock.GetUtcNow().AddDays(1)));
        await admin.PostAsJsonAsync("/admin/announcements", Save("Acum"));

        var anon = App.CreateClient();
        var page = await anon.GetFromJsonAsync<JsonElement>("/announcements");
        Assert.Equal(["acum"], page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slug").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/announcements/ciorna")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/announcements/mai-tarziu")).StatusCode);

        App.Clock.Advance(TimeSpan.FromDays(1));

        var later = await anon.GetFromJsonAsync<JsonElement>("/announcements");
        Assert.Equal(2, later.GetProperty("total").GetInt32());
        Assert.Equal("mai-tarziu", later.GetProperty("items")[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task The_address_follows_the_title_until_the_first_publish()
    {
        var admin = await App.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/admin/announcements", Save("Primul titlu", status: "Draft"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var renamed = await admin.PutAsJsonAsync($"/admin/announcements/{id}", Save("Program de sărbători"));
        Assert.Equal("program-de-sarbatori", (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString());

        var after = await admin.PutAsJsonAsync($"/admin/announcements/{id}", Save("Program de iarnă"));
        Assert.Equal("program-de-sarbatori", (await after.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString());

        var twin = await admin.PostAsJsonAsync("/admin/announcements", Save("Program de sărbători"));
        Assert.Equal("program-de-sarbatori-2", (await twin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("slug").GetString());
    }

    [Fact]
    public async Task A_cover_must_really_be_an_image()
    {
        var admin = await App.AdminClientAsync();

        var fake = await admin.PostAsync("/admin/files/images",
            AdminWorkTests.Upload("poza.jpg", "image/jpeg", "MZ not an image"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, fake.StatusCode);

        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var real = await admin.PostAsync("/admin/files/images", AdminWorkTests.Upload("coperta", "image/png", png));
        Assert.Equal(HttpStatusCode.Created, real.StatusCode);
        var file = await real.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("coperta.png", file.GetProperty("fileName").GetString());

        var cover = Guid.Parse(file.GetProperty("id").GetString()!);
        (await admin.PostAsJsonAsync("/admin/announcements", Save("Cu imagine", cover: cover))).EnsureSuccessStatusCode();

        var item = (await App.CreateClient().GetFromJsonAsync<JsonElement>("/announcements")).GetProperty("items")[0];
        Assert.Equal($"/files/{cover}", item.GetProperty("coverUrl").GetString());
    }

    [Fact]
    public async Task The_home_page_gets_only_the_ones_marked_for_it()
    {
        var admin = await App.AdminClientAsync();
        await admin.PostAsJsonAsync("/admin/announcements", Save("Pe prima pagină"));
        await admin.PostAsJsonAsync("/admin/announcements", Save("Doar în listă", home: false));

        var home = await App.CreateClient().GetFromJsonAsync<JsonElement>("/announcements?home=true");
        Assert.Equal(1, home.GetProperty("total").GetInt32());
    }
}
