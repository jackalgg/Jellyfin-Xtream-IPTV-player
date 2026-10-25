using System.Text.Json;
using Jellyfin.Plugin.StreamFin.Xtream.Json;
using Jellyfin.Plugin.StreamFin.Xtream.Models;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class TolerantJsonTests
{
    [Fact]
    public void Vod_stream_accepts_quoted_ids_and_empty_ratings()
    {
        const string json = """
            {
              "stream_id": "42",
              "name": "The Gift",
              "container_extension": "mkv",
              "category_id": 7,
              "rating": "",
              "tmdb": 12345
            }
            """;

        var stream = JsonSerializer.Deserialize<XtreamVodStream>(json);
        Assert.NotNull(stream);
        Assert.Equal(42, stream!.StreamId);
        Assert.Equal("The Gift", stream.Name);
        Assert.Equal("mkv", stream.ContainerExtension);
        Assert.Equal("7", stream.CategoryId);
        Assert.Null(stream.Rating);
        Assert.Equal("12345", stream.TmdbId);
    }

    [Fact]
    public void Series_detail_accepts_empty_episode_maps_as_arrays()
    {
        const string json = """
            {
              "info": { "name": "Show", "rating": "8.5" },
              "seasons": { "1": { "season_number": "1", "name": "Season 1" } },
              "episodes": {
                "1": [
                  { "id": "1001", "episode_num": "1", "title": "Pilot", "container_extension": "mp4" }
                ]
              }
            }
            """;

        var detail = JsonSerializer.Deserialize<XtreamSeriesDetail>(json);
        Assert.NotNull(detail);
        Assert.Equal("Show", detail!.Info!.Name);
        Assert.Equal(8.5d, detail.Info.Rating);
        Assert.Single(detail.Seasons);
        Assert.True(detail.Episodes.ContainsKey("1"));
        Assert.Equal("1001", detail.Episodes["1"][0].Id);
    }

    [Fact]
    public void Series_detail_accepts_episodes_serialized_as_an_empty_array()
    {
        const string json = """{ "info": { "name": "Empty" }, "seasons": [], "episodes": [] }""";
        var detail = JsonSerializer.Deserialize<XtreamSeriesDetail>(json);
        Assert.NotNull(detail);
        Assert.Empty(detail!.Episodes);
        Assert.Empty(detail.Seasons);
    }
}
