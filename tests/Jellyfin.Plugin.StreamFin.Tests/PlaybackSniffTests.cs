using System.Text;
using Jellyfin.Plugin.StreamFin.Playback;
using Xunit;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class PlaybackSniffTests
{
    [Fact]
    public void Playlist_is_recognized_from_the_body()
    {
        var bytes = Encoding.UTF8.GetBytes("\n#EXTM3U\n#EXTINF:1,\nhttp://panel/1.ts\n");
        Assert.True(PlaybackSniff.IsPlaylist(bytes));
        Assert.True(PlaybackSniff.Accepts("m3u8", "text/html", bytes));
    }

    [Fact]
    public void Html_page_on_an_m3u8_address_is_rejected()
    {
        var bytes = Encoding.UTF8.GetBytes("<html><body>not a stream</body></html>");
        Assert.False(PlaybackSniff.IsPlaylist(bytes));
        Assert.True(PlaybackSniff.IsTextDocument(bytes));
        Assert.False(PlaybackSniff.Accepts("m3u8", "application/vnd.apple.mpegurl", bytes));
        Assert.False(PlaybackSniff.Accepts("mp4", "video/mp4", bytes));
    }

    [Fact]
    public void Json_error_is_rejected()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"user_info\":{\"auth\":0}}");
        Assert.True(PlaybackSniff.IsTextDocument(bytes));
        Assert.False(PlaybackSniff.Accepts("mkv", "application/json", bytes));
    }

    [Fact]
    public void Mp4_webm_and_transport_stream_match_the_container_that_was_asked_for()
    {
        var mp4 = new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p' };
        var webm = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0, 0 };
        var transport = new byte[] { 0x47, 0x40, 0, 0x10 };

        Assert.True(PlaybackSniff.Accepts("mp4", "application/octet-stream", mp4));
        Assert.False(PlaybackSniff.Accepts("m3u8", "application/octet-stream", mp4));
        Assert.True(PlaybackSniff.Accepts("webm", null, webm));
        Assert.False(PlaybackSniff.Accepts("m3u8", "video/mp2t", webm));
        Assert.True(PlaybackSniff.Accepts("ts", "application/octet-stream", transport));
        Assert.Equal("video/mp4", PlaybackSniff.ContentTypeFor("mp4", mp4));
        Assert.Equal("video/webm", PlaybackSniff.ContentTypeFor("webm", webm));
        Assert.Equal("video/mp2t", PlaybackSniff.ContentTypeFor("ts", transport));
        Assert.Equal("application/vnd.apple.mpegurl", PlaybackSniff.ContentTypeFor("m3u8", Encoding.ASCII.GetBytes("#EXTM3U")));
    }

    [Fact]
    public void Bom_does_not_hide_a_playlist()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'#', (byte)'E', (byte)'X', (byte)'T', (byte)'M', (byte)'3', (byte)'U' };
        Assert.True(PlaybackSniff.IsPlaylist(bytes));
        Assert.False(PlaybackSniff.IsTextDocument(bytes));
    }
}
