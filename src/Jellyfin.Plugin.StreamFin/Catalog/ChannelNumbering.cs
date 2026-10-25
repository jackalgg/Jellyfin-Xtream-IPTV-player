using System.Globalization;
using Jellyfin.Plugin.StreamFin.Configuration;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// Works out the channel number shown in the guide.
/// </summary>
public static class ChannelNumbering
{
    /// <summary>
    /// Picks a channel number for one channel.
    /// </summary>
    /// <param name="provider">The owning provider.</param>
    /// <param name="providerNumber">The number the provider assigned, if any.</param>
    /// <param name="position">The channel's 1-based position in the playlist.</param>
    /// <returns>The number to show.</returns>
    /// <remarks>
    /// Two providers both numbering from 1 would interleave in the guide, so a per-provider prefix
    /// keeps each lineup contiguous.
    /// </remarks>
    public static string Resolve(ProviderOptions provider, int? providerNumber, int position)
    {
        var baseNumber = provider.UseProviderChannelNumbers && providerNumber is > 0
            ? providerNumber.Value.ToString(CultureInfo.InvariantCulture)
            : position.ToString(CultureInfo.InvariantCulture);

        var prefix = provider.ChannelNumberPrefix?.Trim();
        return string.IsNullOrEmpty(prefix) ? baseNumber : prefix + baseNumber;
    }

    /// <summary>
    /// Picks a channel number when the source supplied a textual number.
    /// </summary>
    /// <param name="provider">The owning provider.</param>
    /// <param name="providerNumber">The number the provider assigned, if any.</param>
    /// <param name="position">The channel's 1-based position in the playlist.</param>
    /// <returns>The number to show.</returns>
    public static string Resolve(ProviderOptions provider, string? providerNumber, int position)
    {
        if (provider.UseProviderChannelNumbers
            && !string.IsNullOrWhiteSpace(providerNumber)
            && double.TryParse(providerNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            var prefix = provider.ChannelNumberPrefix?.Trim();
            var trimmed = providerNumber.Trim();
            return string.IsNullOrEmpty(prefix) ? trimmed : prefix + trimmed;
        }

        return Resolve(provider, (int?)null, position);
    }
}
