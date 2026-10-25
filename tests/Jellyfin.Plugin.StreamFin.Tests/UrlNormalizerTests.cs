using Jellyfin.Plugin.StreamFin.Configuration;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("http://example.com:8080/", "http://example.com:8080")]
    [InlineData("example.com:8080", "http://example.com:8080")]
    [InlineData("http://example.com:8080/player_api.php?username=a&password=b", "http://example.com:8080")]
    [InlineData("http://example.com/get.php", "http://example.com")]
    [InlineData("https://panel.example.com:443/c", "https://panel.example.com")]
    [InlineData("http://example.com:80/xmltv.php", "http://example.com")]
    public void NormalizeBaseUrl_strips_panel_paths(string input, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.NormalizeBaseUrl(input));
    }

    [Fact]
    public void ResolveAbsolute_joins_relative_paths()
    {
        Assert.Equal(
            "http://example.com:8080/images/poster.jpg",
            UrlNormalizer.ResolveAbsolute("http://example.com:8080", "images/poster.jpg"));
    }

    [Fact]
    public void ResolveAbsolute_keeps_absolute_urls()
    {
        Assert.Equal(
            "https://cdn.example.com/p.jpg",
            UrlNormalizer.ResolveAbsolute("http://example.com", "https://cdn.example.com/p.jpg"));
    }
}
