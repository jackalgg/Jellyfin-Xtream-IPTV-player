using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.StreamFin.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.StreamFin.Tests;

public class PluginConfigurationSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ITestOutputHelper _output;

    public PluginConfigurationSerializationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void XmlSerializer_round_trips_a_configured_provider()
    {
        var config = Sample();
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var stream = new MemoryStream();
        serializer.Serialize(stream, config);
        _output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        stream.Position = 0;
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var round = (PluginConfiguration)serializer.Deserialize(reader)!;

        var provider = Assert.Single(round.Providers);
        Assert.Equal("Mine", provider.Name);
        Assert.Equal(ProviderKind.Xtream, provider.Kind);
        Assert.Equal(FilterMode.Include, provider.MovieFilter.Mode);
        Assert.Equal(new[] { "12", "99" }, provider.MovieFilter.Categories);
        Assert.Equal(new[] { "XXX" }, provider.MovieFilter.TitleExcludePatterns);
        Assert.True(round.ProxyPlayback);
        Assert.False(round.ImportLibraries);
    }

    [Fact]
    public void Old_settings_without_ImportLibraries_stay_off()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <PluginConfiguration>
              <AutoProvision>true</AutoProvision>
              <Providers />
            </PluginConfiguration>
            """;

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var config = (PluginConfiguration)serializer.Deserialize(reader)!;

        Assert.False(config.ImportLibraries);
        Assert.True(config.AutoProvision);
    }

    [Fact]
    public void Json_payload_from_the_settings_page_deserializes()
    {
        const string json = """
            {
              "LibraryRoot": "",
              "AutoProvision": true,
              "SyncIntervalHours": 12,
              "WriteNfoMetadata": true,
              "EnableInternetMetadata": true,
              "PruneRemovedItems": true,
              "CacheMinutes": 60,
              "GuideDays": 7,
              "ProxyPlayback": true,
              "PreferDirectPlay": true,
              "ResolveStreamRedirects": true,
              "RequestTimeoutSeconds": 60,
              "SeriesConcurrency": 4,
              "FetchMovieDetails": true,
              "MovieDetailBudget": 400,
              "AllowUntrustedCertificates": false,
              "VerboseLogging": false,
              "Providers": [
                {
                  "Id": "abc",
                  "Name": "My IPTV",
                  "Enabled": true,
                  "Kind": "Xtream",
                  "ServerUrl": "http://example.com:8080",
                  "Username": "user",
                  "Password": "p&<ass>",
                  "PlaylistUrl": "",
                  "EpgUrl": "",
                  "LiveStreamFormat": "MpegTs",
                  "UserAgent": "",
                  "Referer": "",
                  "MaxConcurrentStreams": 0,
                  "EnableLiveTv": true,
                  "EnableMovies": true,
                  "EnableSeries": true,
                  "UseProviderChannelNumbers": true,
                  "ChannelNumberPrefix": "",
                  "LiveFilter": { "Mode": "All", "Categories": [], "TitleExcludePatterns": [], "MaxItemsPerCategory": 0 },
                  "MovieFilter": { "Mode": "Include", "Categories": ["12"], "TitleExcludePatterns": ["XXX"], "MaxItemsPerCategory": 0 },
                  "SeriesFilter": { "Mode": "Exclude", "Categories": ["9"], "TitleExcludePatterns": [], "MaxItemsPerCategory": 25 }
                }
              ]
            }
            """;

        // Jellyfin's plugin configuration endpoint reads enums as the names the settings page sends.
        var config = JsonSerializer.Deserialize<PluginConfiguration>(json, JsonOptions);
        Assert.NotNull(config);
        Assert.Equal("12", config.Providers[0].MovieFilter.Categories[0]);

        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var stream = new MemoryStream();
        serializer.Serialize(stream, config);
    }

    private static PluginConfiguration Sample() => new()
    {
        ProxyPlayback = true,
        Providers =
        [
            new ProviderOptions
            {
                Name = "Mine",
                Kind = ProviderKind.Xtream,
                ServerUrl = "http://example.com:8080",
                Username = "user",
                Password = "p&<ass>",
                MovieFilter = new CategoryFilter
                {
                    Mode = FilterMode.Include,
                    Categories = ["12", "99"],
                    TitleExcludePatterns = ["XXX"]
                }
            }
        ]
    };
}
