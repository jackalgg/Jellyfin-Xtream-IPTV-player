using Jellyfin.Plugin.StreamFin.Catalog;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class TitleParserTests
{
    [Theory]
    [InlineData("EN - Breaking Bad S01E01 [1080p]", "Breaking Bad", 1, 1, null)]
    [InlineData("Show S1E2 Pilot", "Show", 1, 2, "Pilot")]
    [InlineData("Office S02E03 - Office Olympics", "Office", 2, 3, "Office Olympics")]
    [InlineData("FR| Lupin 1x03", "Lupin", 1, 3, null)]
    [InlineData("Show Name Season 2 Episode 10 The Finale", "Show Name", 2, 10, "The Finale")]
    public void ParseEpisode_reads_season_and_episode(string raw, string series, int season, int episode, string? title)
    {
        var parsed = TitleParser.ParseEpisode(raw);
        Assert.NotNull(parsed);
        Assert.Equal(series, parsed.Value.SeriesTitle);
        Assert.Equal(season, parsed.Value.SeasonNumber);
        Assert.Equal(episode, parsed.Value.EpisodeNumber);
        Assert.Equal(title, parsed.Value.EpisodeTitle);
    }

    [Theory]
    [InlineData("The Gift (2015) 1080p", "The Gift", 2015)]
    [InlineData("FR| Le Samourai (1967) MULTI", "Le Samourai", 1967)]
    [InlineData("Arrival [2016]", "Arrival", 2016)]
    [InlineData("Dune", "Dune", null)]
    public void ParseMovie_strips_year_and_noise(string raw, string title, int? year)
    {
        var parsed = TitleParser.ParseMovie(raw);
        Assert.Equal(title, parsed.Title);
        Assert.Equal(year, parsed.Year);
    }

    [Fact]
    public void ParseEpisode_returns_null_when_there_is_no_marker()
    {
        Assert.Null(TitleParser.ParseEpisode("Just a movie title (2020)"));
    }

    [Fact]
    public void SplitList_drops_blanks_and_duplicates()
    {
        var parts = TitleParser.SplitList("Action, Drama, action, N/A");
        Assert.Equal(new[] { "Action", "Drama" }, parts);
    }

    [Theory]
    [InlineData("2020-05-01", 2020, 5, 1)]
    [InlineData("01/05/2020", 2020, 5, 1)]
    [InlineData("2020", 2020, 1, 1)]
    public void ParseDate_accepts_common_panel_formats(string raw, int year, int month, int day)
    {
        var parsed = TitleParser.ParseDate(raw);
        Assert.NotNull(parsed);
        Assert.Equal(year, parsed.Value.Year);
        Assert.Equal(month, parsed.Value.Month);
        Assert.Equal(day, parsed.Value.Day);
    }
}
