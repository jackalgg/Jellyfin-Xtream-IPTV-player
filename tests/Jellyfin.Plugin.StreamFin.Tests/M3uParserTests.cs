using Jellyfin.Plugin.StreamFin.M3u;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class M3uParserTests
{
    [Fact]
    public void Parse_reads_xtream_m3u_plus_and_classifies_by_path()
    {
        const string playlist = """
            #EXTM3U url-tvg="http://panel.example.com/xmltv.php"
            #EXTINF:-1 tvg-id="bbc1" tvg-name="BBC One" tvg-logo="http://x/bbc.png" group-title="UK",BBC One HD
            http://panel.example.com:8080/live/alice/s3cret/1.ts
            #EXTINF:-1 group-title="Movies",The Gift (2015)
            http://panel.example.com:8080/movie/alice/s3cret/99.mkv
            #EXTINF:-1 group-title="Shows",Breaking Bad S01E01 Pilot
            http://panel.example.com:8080/series/alice/s3cret/1001.mp4
            """;

        var parsed = M3uParser.Parse(playlist);
        Assert.Equal("http://panel.example.com/xmltv.php", parsed.EpgUrl);
        Assert.Equal(3, parsed.Entries.Count);
        Assert.Equal(M3uEntryKind.Live, parsed.Entries[0].Kind);
        Assert.Equal("BBC One HD", parsed.Entries[0].Name);
        Assert.Equal("bbc1", parsed.Entries[0].TvgId);
        Assert.Equal(M3uEntryKind.Movie, parsed.Entries[1].Kind);
        Assert.Equal(M3uEntryKind.Episode, parsed.Entries[2].Kind);
    }

    [Fact]
    public void Parse_reads_vlc_user_agent_and_quoted_commas()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:-1 tvg-name="News, World" group-title="News",News, World
            #EXTVLCOPT:http-user-agent=VLC/3.0.20
            #EXTVLCOPT:http-referrer=http://panel.example.com/
            http://cdn.example.com/live/news.ts
            """;

        var parsed = M3uParser.Parse(playlist);
        Assert.Single(parsed.Entries);
        Assert.Equal("News, World", parsed.Entries[0].Name);
        Assert.Equal("VLC/3.0.20", parsed.Entries[0].UserAgent);
        Assert.Equal("http://panel.example.com/", parsed.Entries[0].Referer);
    }

    [Fact]
    public void Parse_classifies_episode_titles_when_the_url_has_no_hint()
    {
        const string playlist = """
            #EXTM3U
            #EXTINF:2400,EN - Some Show S02E04
            http://cdn.example.com/files/episode.bin
            """;

        var parsed = M3uParser.Parse(playlist);
        Assert.Equal(M3uEntryKind.Episode, parsed.Entries[0].Kind);
    }
}
