using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Speranta.Domain;

namespace Speranta.Api.Tests;

public sealed class FormTests(AppFixture fixture) : AppTest(fixture)
{
    private static object ValidOrder(string book = "biblia", string? website = null) => new
    {
        bookSlug = book,
        fullName = "Ion Popescu",
        phone = "0712 345 678",
        address = "Str. Exemplu 1, Timișoara",
        ageGroup = "From30To40",
        hasBibleAtHome = false,
        acceptsDeliveryArea = true,
        acceptsDataUse = true,
        website,
    };

    [Fact]
    public async Task A_book_order_is_saved_logged_and_announced_by_email()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", ValidOrder());

        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = receipt.GetProperty("reference").GetInt32();
        Assert.True(id > 0);

        var order = App.Db(db => db.BookOrders.Include(o => o.Book).Single());
        Assert.Equal("Biblia", order.Book.Title);
        Assert.Equal(OrderStatus.New, order.Status);
        Assert.Equal(AgeGroup.From30To40, order.AgeGroup);

        var history = App.Db(db => db.Activity.Where(a => a.SubmissionId == id).Select(a => a.Text).ToList());
        Assert.Equal(["Comandă primită de pe site"], history);

        var mail = Assert.Single(App.Outbox());
        Assert.Equal("speranta.azs.tm@gmail.com", mail.To);
        Assert.Contains("Biblia", mail.Subject);
        Assert.Contains($"https://site.test/admin/comenzi/{id}", mail.HtmlBody);
        Assert.Equal((SubmissionKind.BookOrder, id), mail.RecordOn);
    }

    [Fact]
    public async Task What_a_visitor_typed_cannot_become_markup_in_the_notification()
    {
        var order = new
        {
            bookSlug = "biblia",
            fullName = "<script>alert(1)</script>",
            phone = "0712345678",
            address = "<a href=\"https://evil.test\">x</a>",
            ageGroup = "Under20",
            hasBibleAtHome = true,
            acceptsDeliveryArea = true,
            acceptsDataUse = true,
        };

        (await App.CreateClient().PostAsJsonAsync("/forms/book-orders", order)).EnsureSuccessStatusCode();

        var body = Assert.Single(App.Outbox()).HtmlBody;
        Assert.DoesNotContain("<script>", body);
        Assert.DoesNotContain("href=\"https://evil.test\"", body);
        Assert.Contains("&lt;script&gt;", body);
    }

    [Fact]
    public async Task Missing_answers_and_consents_come_back_per_field_in_romanian()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = "biblia",
            phone = "abc",
            acceptsDeliveryArea = false,
            acceptsDataUse = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

        foreach (var field in new[] { "fullName", "phone", "address", "ageGroup", "hasBibleAtHome", "acceptsDeliveryArea", "acceptsDataUse" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"expected an error for {field}");
        }

        Assert.Equal("Numărul de telefon nu pare corect.", errors.GetProperty("phone")[0].GetString());
        Assert.Empty(App.Outbox());
        Assert.Equal(0, App.Db(db => db.BookOrders.Count()));
    }

    [Fact]
    public async Task A_book_switched_off_refuses_orders()
    {
        App.Db(db => db.Books.Where(b => b.Slug == "tragedia-veacurilor")
            .ExecuteUpdate(b => b.SetProperty(x => x.IsAvailable, false)));

        var response = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", ValidOrder("tragedia-veacurilor"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nu mai este în stoc", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_childrens_book_can_be_picked_up_without_an_address_or_age_group()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = "biblia-pentru-elevi",
            fullName = "Maria Ionescu",
            phone = "0723 456 789",
            deliveryMethod = "Pickup",
            acceptsDeliveryArea = true,
            acceptsDataUse = true,
        });

        response.EnsureSuccessStatusCode();
        var order = App.Db(db => db.BookOrders.Single());
        Assert.Equal(DeliveryMethod.Pickup, order.DeliveryMethod);
        Assert.Null(order.Address);
        Assert.Null(order.AgeGroup);
        Assert.Null(order.HasBibleAtHome);
    }

    [Fact]
    public async Task Only_the_childrens_books_offer_pickup()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = "viata-lui-isus",
            fullName = "Ion Popescu",
            phone = "0712 345 678",
            deliveryMethod = "Pickup",
            ageGroup = "Over70",
            acceptsDeliveryArea = true,
            acceptsDataUse = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("deliveryMethod", out _));
    }

    [Fact]
    public async Task Each_book_requires_only_what_its_form_asks()
    {
        // Viața lui Isus asks the age group but not the Bible question.
        var isus = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = "viata-lui-isus",
            fullName = "Ion Popescu",
            phone = "0712 345 678",
            address = "Str. Exemplu 1",
            ageGroup = "Over70",
            hasBibleAtHome = true, // not asked: must not be stored
            acceptsDeliveryArea = true,
            acceptsDataUse = true,
        });
        isus.EnsureSuccessStatusCode();
        Assert.Null(App.Db(db => db.BookOrders.Single().HasBibleAtHome));

        // A children's book delivered still needs the address.
        var elevi = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", new
        {
            bookSlug = "biblia-pentru-elevi",
            fullName = "Ion Popescu",
            phone = "0712 345 678",
            deliveryMethod = "Delivery",
            acceptsDeliveryArea = true,
            acceptsDataUse = true,
        });
        Assert.Equal(HttpStatusCode.BadRequest, elevi.StatusCode);
        var errors = (await elevi.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("address", out _));
        Assert.False(errors.TryGetProperty("ageGroup", out _));
    }

    [Fact]
    public async Task The_site_tells_the_client_what_each_book_form_asks()
    {
        var site = await App.CreateClient().GetFromJsonAsync<JsonElement>("/site");
        var books = site.GetProperty("books").EnumerateArray().ToDictionary(b => b.GetProperty("slug").GetString()!);

        Assert.True(books["biblia"].GetProperty("asksBibleAtHome").GetBoolean());
        Assert.False(books["viata-lui-isus"].GetProperty("asksBibleAtHome").GetBoolean());
        Assert.True(books["biblia-pentru-elevi"].GetProperty("allowsPickup").GetBoolean());
        Assert.True(books["mesajul-lui-dumnezeu-pentru-tine"].GetProperty("isForChildren").GetBoolean());
    }

    [Fact]
    public async Task The_honeypot_answers_like_success_but_keeps_nothing()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/book-orders", ValidOrder(website: "http://spam.test"));

        response.EnsureSuccessStatusCode();
        Assert.Equal(0, App.Db(db => db.BookOrders.Count()));
        Assert.Empty(App.Outbox());
    }

    [Fact]
    public async Task A_kids_signup_needs_no_child_details()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/kids-signups", new
        {
            parentName = "Ana Dumitru",
            phone = "0711 222 333",
            participatedBefore = false,
            acceptsDataUse = true,
        });

        response.EnsureSuccessStatusCode();
        var signup = App.Db(db => db.KidsSignups.Single());
        Assert.Null(signup.ChildFirstName);
        Assert.Null(signup.ChildAge);
        Assert.Single(App.Outbox());
    }

    [Fact]
    public async Task A_kids_signup_rejects_an_impossible_age_and_a_bad_email()
    {
        var response = await App.CreateClient().PostAsJsonAsync("/forms/kids-signups", new
        {
            parentName = "Ana Dumitru",
            phone = "0711 222 333",
            email = "not-an-email",
            childAge = 40,
            participatedBefore = true,
            acceptsDataUse = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("childAge", out _));
        Assert.True(errors.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task A_contact_message_needs_an_email_and_a_message()
    {
        var bad = await App.CreateClient().PostAsJsonAsync("/forms/contact", new { acceptsDataUse = true });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var good = await App.CreateClient().PostAsJsonAsync("/forms/contact", new
        {
            firstName = "George",
            email = "george@exemplu.ro",
            subject = "Program de sărbători",
            message = "Bună ziua!",
            acceptsDataUse = true,
        });

        good.EnsureSuccessStatusCode();
        Assert.Equal("Program de sărbători", App.Db(db => db.ContactMessages.Single().Subject));
    }
}

/// <summary>Its own app, because it needs the real (tiny) form limit. In the shared
/// collection only so it never races the others to create the test database.</summary>
[Collection(AppCollection.Name)]
public sealed class FormRateLimitTests : IAsyncLifetime
{
    private readonly SperantaAppFactory _app = new();

    public async Task InitializeAsync()
    {
        await SperantaAppFactory.EnsureDatabaseAsync();
        _app.Overrides["RateLimits:FormPermitsPerTenMinutes"] = "2";
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task The_forms_stop_a_flood_from_one_address()
    {
        var client = _app.CreateClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 3; i++)
        {
            // Invalid on purpose: the limit applies before validation, and nothing is saved.
            statuses.Add((await client.PostAsJsonAsync("/forms/contact", new { })).StatusCode);
        }

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests], statuses);
    }
}
