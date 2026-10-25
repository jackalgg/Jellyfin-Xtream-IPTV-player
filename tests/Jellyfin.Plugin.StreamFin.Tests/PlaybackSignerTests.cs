using System.Security.Cryptography;
using Jellyfin.Plugin.StreamFin.Playback;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class PlaybackSignerTests
{
    [Fact]
    public void Sign_and_verify_round_trip()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var payload = PlaybackSigner.BuildPayload(PlaybackKind.Movie, "prov", "42", "mp4", "http://x/a.mp4");
        var signature = PlaybackSigner.Sign(key, payload);

        Assert.True(PlaybackSigner.Verify(key, payload, signature));
        Assert.False(PlaybackSigner.Verify(key, payload + "x", signature));
        Assert.False(PlaybackSigner.Verify(key, payload, "not-a-signature"));
        Assert.False(PlaybackSigner.Verify(RandomNumberGenerator.GetBytes(32), payload, signature));
    }

    [Fact]
    public void Encode_and_decode_round_trip()
    {
        const string value = "http://panel.example.com/movie/alice/s3cret/42.mp4?x=1";
        Assert.Equal(value, PlaybackLinkFactory.Decode(PlaybackLinkFactory.Encode(value)));
    }

    [Fact]
    public void TryParse_reads_a_play_url()
    {
        var encoded = PlaybackLinkFactory.Encode("http://panel.example.com/movie/a/b/1.mp4");
        var url = "http://127.0.0.1:8096/StreamFin/Play/movie/abc123/42?c=mp4&u=" + encoded + "&s=deadbeef";
        var parsed = PlaybackLinkFactory.TryParse(url);

        Assert.NotNull(parsed);
        Assert.Equal(PlaybackKind.Movie, parsed!.Kind);
        Assert.Equal("abc123", parsed.ProviderId);
        Assert.Equal("42", parsed.StreamId);
        Assert.Equal("mp4", parsed.Container);
        Assert.Equal("http://panel.example.com/movie/a/b/1.mp4", parsed.UpstreamUrl);
        Assert.Equal("deadbeef", parsed.Signature);
    }

    [Fact]
    public void TryParse_rejects_unrelated_urls()
    {
        Assert.Null(PlaybackLinkFactory.TryParse("http://panel.example.com/movie/a/b/1.mp4"));
        Assert.Null(PlaybackLinkFactory.TryParse(null));
    }

    [Fact]
    public void BuildPayload_normalizes_the_container()
    {
        var a = PlaybackSigner.BuildPayload(PlaybackKind.Movie, "p", "1", ".MP4", "u");
        var b = PlaybackSigner.BuildPayload(PlaybackKind.Movie, "p", "1", "mp4", "u");
        Assert.Equal(a, b);
    }
}
