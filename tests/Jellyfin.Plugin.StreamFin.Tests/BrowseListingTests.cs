using System.Collections.Generic;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Playback;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class BrowseListingTests
{
    [Fact]
    public void Panel_that_mixes_categories_is_detected()
    {
        var rows = new[]
        {
            new Row("12", "In"),
            new Row("99", "Other")
        };

        Assert.True(BrowseListing.PanelReturnedOtherCategories(rows, "12", static row => row.CategoryId));
        Assert.False(BrowseListing.PanelReturnedOtherCategories(rows, "12", static row => "12"));
    }

    [Fact]
    public void Matching_keeps_only_the_opened_category()
    {
        var rows = new[]
        {
            new Row("12", "A"),
            new Row(" 12 ", "B"),
            new Row("99", "C"),
            new Row(null, "D")
        };

        var matched = BrowseListing.Matching(rows, "12", static row => row.CategoryId);

        Assert.Equal(2, matched.Count);
        Assert.Equal("A", matched[0].Name);
        Assert.Equal("B", matched[1].Name);
    }

    [Fact]
    public void Page_filters_by_name_before_slicing()
    {
        var rows = new List<Row>
        {
            new("1", "Alpha"),
            new("1", "Beta"),
            new("1", "Alphabet")
        };

        var page = BrowseListing.Page(rows, "alpha", static row => row.Name, 1, 1);

        Assert.Equal(2, page.Total);
        var item = Assert.Single(page.Items);
        Assert.Equal("Alphabet", item.Name);
    }

    [Theory]
    [InlineData("https://cdn.example/poster.jpg", "https://cdn.example/poster.jpg")]
    [InlineData(" http://cdn.example/a.png ", "http://cdn.example/a.png")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("//cdn.example/a.png", null)]
    [InlineData("data:image/png;base64,aaaa", null)]
    public void SafePoster_allows_only_http_addresses(string input, string? expected)
    {
        Assert.Equal(expected, BrowseListing.SafePoster(input));
    }

    [Theory]
    [InlineData("http://panel/live/1.m3u8", true, "m3u8")]
    [InlineData("http://panel/live/1.ts", true, "ts")]
    [InlineData("http://panel/movie/1.mkv", false, "mkv")]
    [InlineData("http://panel/live/1", true, "ts")]
    [InlineData("http://panel/movie/1", false, "mp4")]
    public void PlaybackContainer_picks_what_the_browser_should_try(string url, bool live, string expected)
    {
        Assert.Equal(expected, BrowseListing.PlaybackContainer(url, live));
    }

    [Fact]
    public void Playlist_segments_stay_relative_to_the_request_path()
    {
        var link = PlaylistPaths.RelativeTo(
            "/jellyfin/StreamFin/Play/live/prov/123",
            "/jellyfin/StreamFin/Play/url/prov/x?c=ts&u=abc");

        Assert.Equal("../../url/prov/x?c=ts&u=abc", link);
    }

    [Fact]
    public void ShortId_is_stable()
    {
        Assert.Equal(BrowseListing.ShortId("http://panel/a"), BrowseListing.ShortId("http://panel/a"));
        Assert.NotEqual(BrowseListing.ShortId("http://panel/a"), BrowseListing.ShortId("http://panel/b"));
        Assert.Equal(16, BrowseListing.ShortId("http://panel/a").Length);
    }

    private sealed record Row(string? CategoryId, string Name);
}
