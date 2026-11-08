using Speranta.Api.Setup;
using Speranta.Domain;

namespace Speranta.Api.Tests;

public sealed class UnitTests
{
    [Theory]
    [InlineData("Program special de sărbători", "program-special-de-sarbatori")]
    [InlineData("  Ziua Bibliei — 2026!  ", "ziua-bibliei-2026")]
    [InlineData("Școală, țară, înșiși", "scoala-tara-insisi")]
    [InlineData("!!!", "anunt")]
    public void Slugs_drop_diacritics_and_punctuation(string title, string expected) =>
        Assert.Equal(expected, Slug.From(title));

    [Theory]
    [InlineData(0, "UnderSeven")]
    [InlineData(6, "UnderSeven")]
    [InlineData(7, "SevenToFourteen")]
    [InlineData(14, "SevenToFourteen")]
    [InlineData(15, null)]
    public void Kids_fall_into_the_sites_two_groups(int age, string? group) =>
        Assert.Equal(group, KidsAge.GroupFor(age));

    [Fact]
    public void This_month_starts_at_midnight_in_Timisoara()
    {
        // 31 Oct 23:30 UTC is already 1 Nov 01:30 in Timișoara (UTC+2 in winter).
        var start = LocalTime.StartOfMonth(DateTimeOffset.Parse("2026-10-31T23:30:00Z"));
        Assert.Equal(DateTimeOffset.Parse("2026-10-31T22:00:00Z"), start);
    }

    [Fact]
    public void Excerpts_are_plain_text_cut_at_a_word()
    {
        var excerpt = Html.Excerpt("<p>Unu <strong>doi</strong> trei patru cinci</p>", 14);
        Assert.Equal("Unu doi trei…", excerpt);
    }
}
