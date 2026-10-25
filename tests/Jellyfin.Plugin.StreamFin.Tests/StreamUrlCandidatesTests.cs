using System.Linq;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Playback;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class StreamUrlCandidatesTests
{
    [Fact]
    public void Build_tries_the_advertised_extension_then_the_usual_fallbacks()
    {
        var provider = new ProviderOptions
        {
            Kind = ProviderKind.Xtream,
            ServerUrl = "http://panel.example.com:8080",
            Username = "alice",
            Password = "s3cret"
        };

        var candidates = StreamUrlCandidates.Build(
            provider,
            PlaybackKind.Movie,
            "42",
            "mkv",
            "http://panel.example.com:8080/movie/alice/s3cret/42.mkv");

        Assert.Equal("http://panel.example.com:8080/movie/alice/s3cret/42.mkv", candidates[0]);
        Assert.Contains("http://panel.example.com:8080/movie/alice/s3cret/42.mp4", candidates);
        Assert.Contains("http://panel.example.com:8080/movie/alice/s3cret/42.ts", candidates);
        Assert.Equal(candidates.Count, candidates.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Build_for_m3u_rewrites_the_original_extension()
    {
        var provider = new ProviderOptions { Kind = ProviderKind.M3u };
        var candidates = StreamUrlCandidates.Build(
            provider,
            PlaybackKind.Movie,
            "abc",
            "mp4",
            "http://cdn.example.com/films/Arrival.mp4");

        Assert.Equal("http://cdn.example.com/films/Arrival.mp4", candidates[0]);
        Assert.Contains("http://cdn.example.com/films/Arrival.mkv", candidates);
    }

    [Fact]
    public void ReplaceExtension_keeps_the_query_string()
    {
        var rewritten = StreamUrlCandidates.ReplaceExtension(
            "http://cdn.example.com/v/1.mp4?token=abc",
            "mkv");

        Assert.Equal("http://cdn.example.com/v/1.mkv?token=abc", rewritten);
    }

    [Fact]
    public void GetExtension_reads_the_path()
    {
        Assert.Equal("mkv", StreamUrlCandidates.GetExtension("http://x/a/b.mkv?y=1"));
        Assert.Null(StreamUrlCandidates.GetExtension("http://x/a/b"));
    }
}
