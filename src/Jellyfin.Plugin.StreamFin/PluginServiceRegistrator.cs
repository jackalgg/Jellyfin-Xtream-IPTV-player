using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Jellyfin.Plugin.StreamFin.Catalog;
using Jellyfin.Plugin.StreamFin.Configuration;
using Jellyfin.Plugin.StreamFin.Epg;
using Jellyfin.Plugin.StreamFin.Library;
using Jellyfin.Plugin.StreamFin.LiveTv;
using Jellyfin.Plugin.StreamFin.Playback;
using Jellyfin.Plugin.StreamFin.State;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.StreamFin;

/// <summary>
/// Hands StreamFin's services to Jellyfin's container.
/// </summary>
/// <remarks>
/// Registering <see cref="ITunerHost"/> and <see cref="IListingsProvider"/> here is what makes them
/// appear in Jellyfin's Live TV setup: the server resolves every registered implementation when it
/// builds its tuner and guide managers, so no further wiring is needed on the server side.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddHttpClient(CatalogService.HttpClientName, ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);

        serviceCollection.AddHttpClient(CatalogService.RedirectProbeHttpClientName, ConfigureClient)
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = CreateHandler();
                handler.AllowAutoRedirect = false;
                return handler;
            });

        serviceCollection.AddHttpClient(PlaybackService.HttpClientName, ConfigurePlaybackClient)
            .ConfigurePrimaryHttpMessageHandler(CreateHandler);

        serviceCollection.AddSingleton<SyncStateStore>();
        serviceCollection.AddSingleton<PlaybackLinkFactory>();
        serviceCollection.AddSingleton<PlaybackService>();
        serviceCollection.AddSingleton<CatalogService>();
        serviceCollection.AddSingleton<XmltvCache>();
        serviceCollection.AddSingleton<LibraryWriter>();
        serviceCollection.AddSingleton<LibraryProvisioner>();
        serviceCollection.AddSingleton<SyncService>();

        serviceCollection.AddSingleton<ITunerHost, StreamFinTunerHost>();
        serviceCollection.AddSingleton<IListingsProvider, StreamFinListingsProvider>();
        serviceCollection.AddSingleton<IMediaSourceProvider, StreamFinMediaSourceProvider>();
    }

    private static PluginConfiguration Options
        => StreamFinPlugin.Instance?.Configuration ?? new PluginConfiguration();

    private static void ConfigureClient(HttpClient client)
    {
        // Panels are slow, and a full VOD listing can be tens of megabytes of JSON.
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(Options.RequestTimeoutSeconds, 10, 600));
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgents.Default);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
    }

    private static void ConfigurePlaybackClient(HttpClient client)
    {
        // A movie can run for hours. The catalog client's short timeout would cut it off.
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgents.Default);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
    }

    private static HttpClientHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,

        // Consulted per connection rather than captured once, so toggling the setting takes effect
        // without waiting for the handler pool to rotate.
        ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || Options.AllowUntrustedCertificates
    };
}
