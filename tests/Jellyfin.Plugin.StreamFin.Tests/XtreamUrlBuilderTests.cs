using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Xtream;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class XtreamUrlBuilderTests
{
    private static XtreamUrlBuilder Builder()
        => new(new ProviderOptions
        {
            ServerUrl = "http://panel.example.com:8080/player_api.php?username=alice&password=s3cret",
            Username = "alice",
            Password = "s3cret"
        });

    [Fact]
    public void Api_builds_the_handshake_and_action_urls()
    {
        var urls = Builder();
        Assert.Equal(
            "http://panel.example.com:8080/player_api.php?username=alice&password=s3cret",
            urls.Api());
        Assert.Equal(
            "http://panel.example.com:8080/player_api.php?username=alice&password=s3cret&action=get_vod_info&vod_id=99",
            urls.Api("get_vod_info", "vod_id", "99"));
    }

    [Fact]
    public void Stream_urls_use_the_advertised_container()
    {
        var urls = Builder();
        Assert.Equal(
            "http://panel.example.com:8080/movie/alice/s3cret/42.mkv",
            urls.MovieStream(42, "mkv"));
        Assert.Equal(
            "http://panel.example.com:8080/series/alice/s3cret/1001.mp4",
            urls.EpisodeStream("1001", null));
        Assert.Equal(
            "http://panel.example.com:8080/live/alice/s3cret/7.ts",
            urls.LiveStream(7, StreamFormat.MpegTs));
        Assert.Equal(
            "http://panel.example.com:8080/live/alice/s3cret/7.m3u8",
            urls.LiveStream(7, StreamFormat.Hls));
    }

    [Fact]
    public void Redact_hides_credentials()
    {
        var urls = Builder();
        var redacted = urls.Redact(urls.MovieStream(1, "mp4"));
        Assert.DoesNotContain("alice", redacted, System.StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", redacted, System.StringComparison.Ordinal);
        Assert.Contains("user", redacted, System.StringComparison.Ordinal);
        Assert.Contains("***", redacted, System.StringComparison.Ordinal);
    }
}
