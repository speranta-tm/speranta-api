using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Speranta.Api.Tests;

public sealed class AdminWorkTests(AppFixture fixture) : AppTest(fixture)
{
    private async Task<int> OrderAsync(string name = "Ion Popescu", string book = "biblia")
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = book,
            fullName = name,
            phone = "0712 345 678",
            address = "Str. Exemplu 1",
            ageGroup = "Under20",
            hasBibleAtHome = true,
            acceptsDeliveryArea = true,
            acceptsDataUse = true,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reference").GetInt32();
    }

    [Fact]
    public async Task Changing_an_order_status_is_kept_in_its_history_with_who_did_it()
    {
        var id = await OrderAsync();
        var admin = await App.AdminClientAsync();

        var response = await admin.PatchAsJsonAsync($"/admin/book-orders/{id}",
            new { status = "Confirmed", internalNote = "Curier: Daniel." });
        response.EnsureSuccessStatusCode();

        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Confirmed", detail.GetProperty("status").GetString());
        Assert.Equal("Curier: Daniel.", detail.GetProperty("internalNote").GetString());

        var history = detail.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("text").GetString()).ToList();
        Assert.Equal(["Comandă primită de pe site", "Status: Confirmat · Marc"], history);
    }

    [Fact]
    public async Task Switching_to_pickup_lets_the_address_go()
    {
        var id = await OrderAsync();
        var admin = await App.AdminClientAsync();

        var clearWhileDelivering = await admin.PatchAsJsonAsync($"/admin/book-orders/{id}", new { address = "" });
        Assert.Equal(HttpStatusCode.BadRequest, clearWhileDelivering.StatusCode);

        var pickup = await admin.PatchAsJsonAsync($"/admin/book-orders/{id}", new { deliveryMethod = "Pickup", address = "" });
        pickup.EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await pickup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("address").ValueKind);
    }

    [Fact]
    public async Task The_order_list_filters_searches_and_counts_every_status()
    {
        var first = await OrderAsync("Ion Popescu");
        await OrderAsync("Maria Ionescu", "biblia-pentru-elevi");
        var admin = await App.AdminClientAsync();
        await admin.PatchAsJsonAsync($"/admin/book-orders/{first}", new { status = "Cancelled" });

        var all = await admin.GetFromJsonAsync<JsonElement>("/admin/book-orders");
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.Equal(1, all.GetProperty("counts").GetProperty("New").GetInt32());
        Assert.Equal(0, all.GetProperty("counts").GetProperty("Delivered").GetInt32());

        var cancelled = await admin.GetFromJsonAsync<JsonElement>("/admin/book-orders?status=Cancelled");
        Assert.Equal("Ion Popescu", cancelled.GetProperty("items")[0].GetProperty("fullName").GetString());

        var search = await admin.GetFromJsonAsync<JsonElement>("/admin/book-orders?q=ionescu");
        Assert.Equal("biblia-pentru-elevi", search.GetProperty("items")[0].GetProperty("bookSlug").GetString());
    }

    [Fact]
    public async Task The_dashboard_lists_what_is_still_new()
    {
        await OrderAsync();
        (await App.CreateClient().PostAsJsonAsync("/forms/kids-signups", new
        {
            parentName = "Ana Dumitru", phone = "0711222333", childFirstName = "Andrei", childAge = 6,
            participatedBefore = false, acceptsDataUse = true,
        })).EnsureSuccessStatusCode();

        var admin = await App.AdminClientAsync();
        var dash = await admin.GetFromJsonAsync<JsonElement>("/admin/dashboard");

        Assert.Equal("Marc", dash.GetProperty("displayName").GetString());
        Assert.Equal(1, dash.GetProperty("newOrders").GetInt32());
        Assert.Equal(1, dash.GetProperty("newSignups").GetInt32());
        var titles = dash.GetProperty("todo").EnumerateArray().Select(t => t.GetProperty("title").GetString()).ToList();
        Assert.Contains("Ana Dumitru · Andrei, 6 ani", titles);
        Assert.Contains("Ion Popescu · Biblia", titles);
    }

    [Fact]
    public async Task Kids_signups_show_the_group_their_age_falls_in()
    {
        foreach (var age in new int?[] { 4, 9, null })
        {
            (await App.CreateClient().PostAsJsonAsync("/forms/kids-signups", new
            {
                parentName = "Părinte", phone = "0711222333", childAge = age,
                participatedBefore = false, acceptsDataUse = true,
            })).EnsureSuccessStatusCode();
        }

        var admin = await App.AdminClientAsync();
        var list = await admin.GetFromJsonAsync<JsonElement>("/admin/kids-signups");
        var groups = list.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("childAge").ValueKind == JsonValueKind.Null ? (int?)null : i.GetProperty("childAge").GetInt32(),
                i.GetProperty("group").GetString()))
            .ToList();

        Assert.Contains((4, "UnderSeven"), groups);
        Assert.Contains((9, "SevenToFourteen"), groups);
        Assert.Contains((null, null), groups);
    }

    [Fact]
    public async Task The_schedule_replaces_whole_and_shows_on_the_site()
    {
        var admin = await App.AdminClientAsync();

        var bad = await admin.PutAsJsonAsync("/admin/schedule", new
        {
            services = new[] { new { day = "Friday", start = "21:00", end = "20:00" } },
            kidsGroups = Array.Empty<object>(),
            kidsPhone = "0764 915 396",
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        (await admin.PutAsJsonAsync("/admin/schedule", new
        {
            services = new[]
            {
                new { day = "Saturday", start = "09:30", end = "12:00" },
                new { day = "Wednesday", start = "19:00", end = "20:00" },
            },
            kidsGroups = new[] { new { label = "Toți copiii", start = "10:00", end = "10:50" } },
            kidsPhone = "0700 000 000",
        })).EnsureSuccessStatusCode();

        var site = await App.CreateClient().GetFromJsonAsync<JsonElement>("/site");
        var services = site.GetProperty("services");
        Assert.Equal(2, services.GetArrayLength());
        Assert.Equal("Saturday", services[0].GetProperty("day").GetString());
        Assert.Equal("09:30:00", services[0].GetProperty("start").GetString());
        Assert.Equal("Toți copiii", site.GetProperty("kidsGroups")[0].GetProperty("label").GetString());
        Assert.Equal("0700 000 000", site.GetProperty("kidsPhone").GetString());
    }

    [Fact]
    public async Task Books_can_be_switched_off_and_reordered()
    {
        var admin = await App.AdminClientAsync();
        var books = await admin.GetFromJsonAsync<JsonElement>("/admin/books");
        var ids = books.EnumerateArray().Select(b => b.GetProperty("id").GetInt32()).ToList();

        (await admin.PatchAsJsonAsync($"/admin/books/{ids[5]}", new { isAvailable = false })).EnsureSuccessStatusCode();
        ids.Reverse();
        (await admin.PutAsJsonAsync("/admin/books/order", new { ids })).EnsureSuccessStatusCode();

        var site = await App.CreateClient().GetFromJsonAsync<JsonElement>("/site");
        var first = site.GetProperty("books")[0];
        Assert.Equal("mesajul-lui-dumnezeu-pentru-tine", first.GetProperty("slug").GetString());
        Assert.False(first.GetProperty("isAvailable").GetBoolean());

        var partial = await admin.PutAsJsonAsync("/admin/books/order", new { ids = new[] { 1, 2 } });
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
    }

    [Fact]
    public async Task The_flyer_must_be_a_pdf_and_its_downloads_are_counted()
    {
        var admin = await App.AdminClientAsync();

        var notPdf = await admin.PostAsync("/admin/flyer", Upload("pliant.pdf", "application/pdf", "<html>nope</html>"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, notPdf.StatusCode);

        var pdf = "%PDF-1.7\n%fake but the header is right\n"u8.ToArray();
        (await admin.PostAsync("/admin/flyer", Upload("pliant-noiembrie-2026.pdf", "application/pdf", pdf))).EnsureSuccessStatusCode();

        var anon = App.CreateClient(new() { AllowAutoRedirect = false });
        var redirect = await anon.GetAsync("/flyer");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);

        var file = await anon.GetAsync(redirect.Headers.Location);
        file.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal(pdf, await file.Content.ReadAsByteArrayAsync());

        var flyer = await admin.GetFromJsonAsync<JsonElement>("/admin/flyer");
        Assert.Equal(1, flyer.GetProperty("downloads").GetInt32());
        Assert.Equal("pliant-noiembrie-2026.pdf", flyer.GetProperty("fileName").GetString());

        var site = await anon.GetFromJsonAsync<JsonElement>("/site");
        Assert.Equal("/flyer", site.GetProperty("flyer").GetProperty("url").GetString());
    }

    internal static MultipartFormDataContent Upload(string name, string type, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { content, "file", name } };
    }
}
