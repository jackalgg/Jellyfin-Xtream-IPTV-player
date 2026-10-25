using System.Linq;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Playback;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class FfmpegRemuxArgumentsTests
{
    [Fact]
    public void Copy_passes_the_file_and_player_headers_without_a_shell()
    {
        var provider = new ProviderOptions
        {
            ServerUrl = "http://panel.example:8080",
            Username = "user",
            Password = "secret",
            UserAgent = "Player\r\nX-Injected: yes",
            Referer = "http://panel.example/app\nX-Evil: 1"
        };
        var source = "http://panel.example:8080/movie/user/secret/9.mkv";

        var args = FfmpegRemuxArguments.Copy(provider, source);

        var input = args.ToList().IndexOf("-i");
        Assert.True(input > 0);
        Assert.Equal(source, args[input + 1]);
        Assert.Contains("-c:v", args);
        Assert.Equal("copy", args[args.ToList().IndexOf("-c:v") + 1]);
        Assert.Contains("1000000", args);
        Assert.Equal("Player", args[args.ToList().IndexOf("-user_agent") + 1]);

        var headers = args[args.ToList().IndexOf("-headers") + 1];
        Assert.DoesNotContain("secret", headers);
        Assert.DoesNotContain("X-Evil", headers);
        Assert.DoesNotContain("X-Injected", headers);
        Assert.Contains("Referer: http://panel.example/app", headers);
    }

    [Fact]
    public void Transcode_uses_a_fast_h264_encoder()
    {
        var provider = new ProviderOptions
        {
            ServerUrl = "http://panel.example:8080",
            Username = "user",
            Password = "secret"
        };

        var args = FfmpegRemuxArguments.Transcode(provider, "http://panel.example:8080/movie/user/secret/9.mkv");

        Assert.Equal("libx264", args[args.ToList().IndexOf("-c:v") + 1]);
        Assert.Equal("ultrafast", args[args.ToList().IndexOf("-preset") + 1]);
        Assert.Equal("pipe:1", args[^1]);
        Assert.DoesNotContain(args, argument => argument.Contains("secret") && argument.StartsWith('-'));
    }
}
