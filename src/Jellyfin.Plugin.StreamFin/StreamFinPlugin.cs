using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.StreamFin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.StreamFin;

/// <summary>
/// StreamFin's plugin entry point.
/// </summary>
public class StreamFinPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The plugin's identifier, which must stay fixed for upgrades to be recognized.
    /// </summary>
    public const string PluginGuid = "cdf3b2f9-ba0b-41f1-8ebc-e340588673ab";

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamFinPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server's path layout.</param>
    /// <param name="xmlSerializer">The configuration serializer.</param>
    public StreamFinPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the loaded instance.
    /// </summary>
    public static StreamFinPlugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "StreamFin IPTV";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <inheritdoc />
    public override string Description =>
        "A player for an Xtream Codes or M3U subscription you already have. StreamFin does not include channels.";

    /// <summary>
    /// Gets the directory the generated <c>.strm</c> and <c>.nfo</c> files are written to.
    /// </summary>
    /// <returns>An absolute directory path.</returns>
    public string GetLibraryRoot()
    {
        var configured = Configuration.LibraryRoot;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        // Default inside the server's data directory so it survives container restarts and is
        // already covered by whatever the user backs up.
        return Path.Combine(ApplicationPaths.DataPath, "streamfin", "media");
    }

    /// <summary>
    /// Gets the directory the plugin keeps its sync state and caches in.
    /// </summary>
    /// <returns>An absolute directory path.</returns>
    public string GetStateDirectory()
    {
        var path = Path.Combine(ApplicationPaths.DataPath, "streamfin", "state");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var prefix = GetType().Namespace + ".Configuration.Web.";

        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = prefix + "configPage.html",
            DisplayName = "StreamFin IPTV",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "live_tv"
        };

        yield return new PluginPageInfo
        {
            Name = "configPage.js",
            EmbeddedResourcePath = prefix + "configPage.js"
        };

        yield return new PluginPageInfo
        {
            Name = "StreamFinWatch",
            EmbeddedResourcePath = prefix + "playerPage.html",
            DisplayName = "Watch",
            EnableInMainMenu = true,
            MenuSection = "server",
            MenuIcon = "play_arrow"
        };

        yield return new PluginPageInfo
        {
            Name = "playerPage.js",
            EmbeddedResourcePath = prefix + "playerPage.js"
        };

        // Bundled so playback does not depend on a CDN or a dashboard script policy.
        yield return new PluginPageInfo
        {
            Name = "streamfin-hls.js",
            EmbeddedResourcePath = prefix + "streamfin-hls.js"
        };

        yield return new PluginPageInfo
        {
            Name = "streamfin-mpegts.js",
            EmbeddedResourcePath = prefix + "streamfin-mpegts.js"
        };
    }

    /// <summary>
    /// Finds a configured provider by identifier.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <returns>The provider, or <c>null</c> when it is not configured.</returns>
    public ProviderOptions? FindProvider(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        foreach (var provider in Configuration.Providers)
        {
            if (string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the Live TV tuner identifier StreamFin uses for one provider.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>A stable tuner identifier.</returns>
    public static string BuildTunerId(ProviderOptions provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return string.Create(CultureInfo.InvariantCulture, $"streamfin-{provider.Id}");
    }
}
