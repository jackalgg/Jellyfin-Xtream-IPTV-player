using Jellyfin.Plugin.StreamFin.Library;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class PathSanitizerTests
{
    [Fact]
    public void Sanitize_strips_illegal_characters()
    {
        Assert.Equal("Foo Bar", PathSanitizer.Sanitize("Foo: Bar?"));
        Assert.Equal("CON_", PathSanitizer.Sanitize("CON"));
        Assert.Equal("Unknown", PathSanitizer.Sanitize("   "));
    }

    [Fact]
    public void BuildTitleSegment_includes_the_year()
    {
        Assert.Equal("Arrival (2016)", PathSanitizer.BuildTitleSegment("Arrival", 2016));
        Assert.Equal("Dune", PathSanitizer.BuildTitleSegment("Dune", null));
    }

    [Fact]
    public void BuildEpisodeSegment_keeps_the_marker()
    {
        Assert.Equal(
            "Breaking Bad (2008) S01E01 - Pilot",
            PathSanitizer.BuildEpisodeSegment("Breaking Bad (2008)", 1, 1, "Pilot"));
        Assert.Equal("Specials", PathSanitizer.BuildSeasonSegment(0));
        Assert.Equal("Season 02", PathSanitizer.BuildSeasonSegment(2));
    }
}
