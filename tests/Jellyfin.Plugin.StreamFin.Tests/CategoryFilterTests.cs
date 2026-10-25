using Jellyfin.Plugin.StreamFin.Configuration;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class CategoryFilterTests
{
    [Fact]
    public void AllowsCategory_include_matches_id_or_name()
    {
        var filter = new CategoryFilter
        {
            Mode = FilterMode.Include,
            Categories = new[] { "12", "Movies" }
        };

        Assert.True(filter.AllowsCategory("12", "Films"));
        Assert.True(filter.AllowsCategory("99", "Movies"));
        Assert.False(filter.AllowsCategory("99", "Kids"));
    }

    [Fact]
    public void AllowsTitle_treats_plain_words_as_regex()
    {
        var filter = new CategoryFilter
        {
            TitleExcludePatterns = new[] { "XXX", "4K" }
        };

        Assert.False(filter.AllowsTitle("Some Title XXX"));
        Assert.False(filter.AllowsTitle("Film 4K"));
        Assert.True(filter.AllowsTitle("Ordinary Film"));
    }

    [Fact]
    public void AllowsTitle_ignores_a_broken_pattern()
    {
        var filter = new CategoryFilter
        {
            TitleExcludePatterns = new[] { "(" }
        };

        Assert.True(filter.AllowsTitle("Anything"));
    }
}
