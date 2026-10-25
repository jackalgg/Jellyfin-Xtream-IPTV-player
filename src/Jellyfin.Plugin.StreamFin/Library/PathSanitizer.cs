using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.StreamFin.Library;

/// <summary>
/// Turns provider titles into path segments that are legal on every platform Jellyfin runs on.
/// </summary>
public static class PathSanitizer
{
    private const int MaxSegmentLength = 120;

    // The union of what Windows, macOS, and Linux reject, so a library written on one host can be
    // mounted on another without renaming anything.
    private static readonly char[] _invalid = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    private static readonly string[] _reservedWindowsNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Makes one path segment safe.
    /// </summary>
    /// <param name="value">The raw segment.</param>
    /// <param name="fallback">What to return when nothing usable survives.</param>
    /// <returns>A safe segment.</returns>
    public static string Sanitize(string? value, string fallback = "Unknown")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (char.IsControl(c) || _invalid.Contains(c))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(c);
            }
        }

        var collapsed = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Windows silently strips trailing dots and spaces, which would make the path Jellyfin
        // recorded disagree with the one on disk.
        collapsed = collapsed.TrimEnd('.', ' ');

        if (collapsed.Length > MaxSegmentLength)
        {
            collapsed = collapsed[..MaxSegmentLength].TrimEnd('.', ' ');
        }

        if (collapsed.Length == 0)
        {
            return fallback;
        }

        foreach (var reserved in _reservedWindowsNames)
        {
            if (string.Equals(collapsed, reserved, StringComparison.OrdinalIgnoreCase))
            {
                return collapsed + "_";
            }
        }

        return collapsed;
    }

    /// <summary>
    /// Builds the folder and file stem Jellyfin expects for a movie or series.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="year">The release year, when one is known.</param>
    /// <returns>A segment such as <c>Arrival (2016)</c>.</returns>
    /// <remarks>
    /// Including the year is what lets Jellyfin's matchers pick the right TMDB entry for the dozen
    /// films called "The Gift".
    /// </remarks>
    public static string BuildTitleSegment(string? title, int? year)
    {
        var safeTitle = Sanitize(title);

        if (year is null or < 1870 or > 2200)
        {
            return safeTitle;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{safeTitle} ({year})");
    }

    /// <summary>
    /// Builds the season folder name.
    /// </summary>
    /// <param name="seasonNumber">The season number.</param>
    /// <returns><c>Specials</c> for season zero, otherwise <c>Season 01</c>.</returns>
    public static string BuildSeasonSegment(int seasonNumber)
        => seasonNumber <= 0
            ? "Specials"
            : string.Create(CultureInfo.InvariantCulture, $"Season {seasonNumber:00}");

    /// <summary>
    /// Builds the episode file stem.
    /// </summary>
    /// <param name="seriesSegment">The series folder name.</param>
    /// <param name="seasonNumber">The season number.</param>
    /// <param name="episodeNumber">The episode number.</param>
    /// <param name="episodeTitle">The episode title, when one is known.</param>
    /// <returns>A stem such as <c>Breaking Bad (2008) S01E01 - Pilot</c>.</returns>
    /// <remarks>
    /// The <c>SxxEyy</c> marker is what Jellyfin's episode resolver keys on, so it is never
    /// omitted, and the episode title is appended only when it adds something.
    /// </remarks>
    public static string BuildEpisodeSegment(string seriesSegment, int seasonNumber, int episodeNumber, string? episodeTitle)
    {
        var stem = string.Create(
            CultureInfo.InvariantCulture,
            $"{seriesSegment} S{Math.Max(seasonNumber, 0):00}E{Math.Max(episodeNumber, 0):00}");

        if (string.IsNullOrWhiteSpace(episodeTitle))
        {
            return stem;
        }

        var safeTitle = Sanitize(episodeTitle, fallback: string.Empty);
        if (safeTitle.Length == 0)
        {
            return stem;
        }

        var combined = stem + " - " + safeTitle;
        return combined.Length > MaxSegmentLength + 40
            ? combined[..(MaxSegmentLength + 40)].TrimEnd('.', ' ', '-')
            : combined;
    }
}
