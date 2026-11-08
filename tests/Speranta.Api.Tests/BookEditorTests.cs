using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Speranta.Api.Tests;

public sealed class BookEditorTests(AppFixture fixture) : AppTest(fixture)
{
    private async Task<Guid> CoverAsync(HttpClient admin)
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
        var res = await admin.PostAsync("/admin/files/images", AdminWorkTests.Upload("coperta.png", "image/png", png));
        res.EnsureSuccessStatusCode();
        return Guid.Parse((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!);
    }

    private static object NewBook(Guid? cover, string title = "Mesajul pentru familie") => new
    {
        title,
        blurb = "Povestiri pentru toată familia.",
        lead = "O carte de citit împreună.",
        paragraphs = new[] { "Primul paragraf.", "  ", "Al doilea paragraf." },
        benefits = new[] { "Gratuită" },
        coverFileId = cover,
        isAvailable = true,
        allowsPickup = true,
        isForChildren = true,
    };

    [Fact]
    public async Task The_launch_books_are_seeded_with_their_page_text()
    {
        var page = await App.CreateClient().GetFromJsonAsync<JsonElement>("/books/biblia");
        Assert.Equal("Biblia sau Sfânta Scriptură", page.GetProperty("heading").GetString());
        Assert.Equal("Cauți sens? Răspunsuri? Vrei să afli ce rezervă viitorul?", page.GetProperty("lead").GetString());
        Assert.Equal(3, page.GetProperty("benefits").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("coverUrl").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await App.CreateClient().GetAsync("/books/nu-exista")).StatusCode);
    }

    [Fact]
    public async Task An_admin_adds_a_book_that_gets_a_page_and_can_be_ordered()
    {
        var admin = await App.AdminClientAsync();
        var created = await admin.PostAsJsonAsync("/admin/books", NewBook(await CoverAsync(admin)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var book = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("mesajul-pentru-familie", book.GetProperty("slug").GetString());
        Assert.Equal("Primește gratuit „Mesajul pentru familie”", book.GetProperty("formTitle").GetString());
        Assert.Equal(2, book.GetProperty("paragraphs").GetArrayLength()); // the blank one is dropped

        var site = await App.CreateClient().GetFromJsonAsync<JsonElement>("/site");
        var last = site.GetProperty("books").EnumerateArray().Last();
        Assert.Equal("mesajul-pentru-familie", last.GetProperty("slug").GetString());
        Assert.StartsWith("/files/", last.GetProperty("coverUrl").GetString());

        var page = await App.CreateClient().GetFromJsonAsync<JsonElement>("/books/mesajul-pentru-familie");
        Assert.Equal("Mesajul pentru familie", page.GetProperty("heading").GetString());

        (await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = "mesajul-pentru-familie", fullName = "Ana", phone = "0711222333", deliveryMethod = "Pickup",
            acceptsDeliveryArea = true, acceptsDataUse = true,
        })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_new_book_needs_a_title_text_and_cover()
    {
        var admin = await App.AdminClientAsync();
        var res = await admin.PostAsJsonAsync("/admin/books", new { isAvailable = true });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        foreach (var f in new[] { "title", "blurb", "lead", "coverFileId" })
        {
            Assert.True(errors.TryGetProperty(f, out _), f);
        }
    }

    // Edits Tragedia veacurilor (id 3), which no other test reads, because the test
    // database's book texts are not reset between tests.
    [Fact]
    public async Task Editing_keeps_the_address_and_a_launch_books_shipped_cover()
    {
        var admin = await App.AdminClientAsync();
        var current = await admin.GetFromJsonAsync<JsonElement>("/admin/books/3");
        var res = await admin.PutAsJsonAsync("/admin/books/3", new
        {
            title = "Tragedia veacurilor (ediție nouă)",
            heading = current.GetProperty("heading").GetString(),
            blurb = current.GetProperty("blurb").GetString(),
            lead = "Un nou început.",
            paragraphs = new[] { "Text." },
            isAvailable = true,
            asksAgeGroup = true,
        });
        res.EnsureSuccessStatusCode();
        var saved = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("tragedia-veacurilor", saved.GetProperty("slug").GetString());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("coverFileId").ValueKind);

        var page = await App.CreateClient().GetFromJsonAsync<JsonElement>("/books/tragedia-veacurilor");
        Assert.Equal("Un nou început.", page.GetProperty("lead").GetString());
    }

    [Fact]
    public async Task A_book_with_orders_cannot_be_deleted_but_a_fresh_one_can()
    {
        var admin = await App.AdminClientAsync();
        (await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = "calea-catre-hristos", fullName = "Ion", phone = "0712345678", address = "Str. 1",
            ageGroup = "Over70", acceptsDeliveryArea = true, acceptsDataUse = true,
        })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync("/admin/books/4")).StatusCode);

        var created = await (await admin.PostAsJsonAsync("/admin/books", NewBook(await CoverAsync(admin), "De șters")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/admin/books/{created.GetProperty("id").GetInt32()}")).StatusCode);
    }
}
