using System;
using System.Collections.Generic;
using System.Text;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Builds playlist-relative links so a segment stays on the host that served the playlist.
/// </summary>
internal static class PlaylistPaths
{
    /// <summary>
    /// Returns <paramref name="toPath"/> as a reference relative to the directory of <paramref name="fromFilePath"/>.
    /// </summary>
    /// <param name="fromFilePath">The playlist request path, without a query.</param>
    /// <param name="toPath">The target path, optionally followed by a query string.</param>
    /// <returns>A relative reference.</returns>
    public static string RelativeTo(string fromFilePath, string toPath)
    {
        ArgumentNullException.ThrowIfNull(fromFilePath);
        ArgumentNullException.ThrowIfNull(toPath);

        var queryIndex = toPath.IndexOf('?', StringComparison.Ordinal);
        var toPathOnly = queryIndex >= 0 ? toPath[..queryIndex] : toPath;
        var query = queryIndex >= 0 ? toPath[queryIndex..] : string.Empty;

        var fromSlash = fromFilePath.LastIndexOf('/');
        var fromDir = fromSlash >= 0 ? fromFilePath[..fromSlash] : string.Empty;
        var fromParts = Split(fromDir);
        var toParts = Split(toPathOnly);

        var common = 0;
        while (common < fromParts.Count
            && common < toParts.Count
            && string.Equals(fromParts[common], toParts[common], StringComparison.Ordinal))
        {
            common++;
        }

        var builder = new StringBuilder();
        for (var i = common; i < fromParts.Count; i++)
        {
            builder.Append("../");
        }

        for (var i = common; i < toParts.Count; i++)
        {
            if (builder.Length > 0 && builder[^1] != '/')
            {
                builder.Append('/');
            }

            builder.Append(toParts[i]);
        }

        if (builder.Length == 0)
        {
            builder.Append('.');
        }

        builder.Append(query);
        return builder.ToString();
    }

    private static List<string> Split(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            parts.Add(part);
        }

        return parts;
    }
}
