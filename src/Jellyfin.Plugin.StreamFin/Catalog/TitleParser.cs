using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.StreamFin.Catalog;

/// <summary>
/// The pieces recovered from a provider's movie title.
/// </summary>
/// <param name="Title">The cleaned title.</param>
/// <param name="Year">The release year, when one was present.</param>
/// <param name="LanguageTag">A leading language or region tag, when one was present.</param>
public readonly record struct ParsedMovieTitle(string Title, int? Year, string? LanguageTag);

/// <summary>
/// The pieces recovered from a provider's episode title.
/// </summary>
/// <param name="SeriesTitle">The cleaned series title.</param>
/// <param name="SeasonNumber">The season number.</param>
/// <param name="EpisodeNumber">The episode number.</param>
/// <param name="EpisodeTitle">The episode title, when one was present.</param>
/// <param name="Year">The series year, when one was present.</param>
/// <param name="LanguageTag">A leading language or region tag, when one was present.</param>
public readonly record struct ParsedEpisodeTitle(
    string SeriesTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string? EpisodeTitle,
    int? Year,
    string? LanguageTag);

/// <summary>
/// Pulls structure out of the free-form titles IPTV providers use.
/// </summary>
/// <remarks>
/// Provider titles look like <c>EN - Breaking Bad S01E01 [1080p]</c> or <c>FR| Le Samourai (1967) MULTI</c>.
/// Handing those to Jellyfin verbatim produces a library of unmatched one-off entries, which is the
/// single biggest reason IPTV libraries look broken. Everything here exists to get back to
/// <c>Breaking Bad</c>, season 1, episode 1.
/// </remarks>
public static class TitleParser
{
    private static readonly TimeSpan _regexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly char[] ListSeparators = { ',', '/', '|', ';' };

    // "EN -", "|FR|", "[DE]", "(AR)", "US:", "EN " at the very start of a title.
    private static readonly Regex _leadingTag = new(
        @"^\s*[\[\(\|]?\s*(?<tag>[A-Z]{2,4}(?:[/\-][A-Z]{2,4})?)\s*[\]\)\|]?\s*(?:[-–:|]\s*|\s+(?=[A-Z0-9]))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        _regexTimeout);

    // S01E02, S1 E2, s01.e02, S01xE02, and the dash-range form S01E02-E04.
    private static readonly Regex _seasonEpisode = new(
        @"(?<![A-Za-z0-9])S\s*(?<season>\d{1,3})\s*[\.\-_x ]?\s*E\s*(?<episode>\d{1,4})(?:\s*[\-–]\s*E?\d{1,4})?(?![0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        _regexTimeout);

    // 1x02 and 01x002.
    private static readonly Regex _crossEpisode = new(
        @"(?<![A-Za-z0-9])(?<season>\d{1,3})\s*x\s*(?<episode>\d{1,4})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        _regexTimeout);

    // "Season 2 Episode 10", "Saison 2 Episode 10", "Temporada 2 Capitulo 10".
    private static readonly Regex _wordyEpisode = new(
        @"(?<![A-Za-z])(?:season|saison|temporada|staffel|stagione)\s*(?<season>\d{1,3})\s*[-–:,]?\s*(?:episode|episodio|capitulo|cap[ií]tulo|folge|ep)\s*\.?\s*(?<episode>\d{1,4})(?![0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        _regexTimeout);

    // A year in brackets, parentheses, or standing alone at the end of the title.
    private static readonly Regex _bracketedYear = new(
        @"[\[\(](?<year>(?:19|20)\d{2})[\]\)]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _trailingYear = new(
        @"(?<![\d])(?<year>(?:19|20)\d{2})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        _regexTimeout);

    // Release and quality noise that should never end up in a library title.
    private static readonly Regex _qualityNoise = new(
        @"(?<![A-Za-z0-9])(?:" +
        @"4k|uhd|fhd|hd|sd|hq|lq|sdr|hdr10\+?|hdr|dolby\s*vision|dv|" +
        @"[0-9]{3,4}[pi]|[0-9]{3,4}x[0-9]{3,4}|" +
        @"x\.?26[45]|h\.?26[45]|hevc|avc|xvid|divx|av1|" +
        @"web[\-\.]?dl|web[\-\.]?rip|webrip|web|bd[\-\.]?rip|br[\-\.]?rip|blu[\-\.]?ray|bluray|hd[\-\.]?rip|dvd[\-\.]?rip|dvdrip|hdtv|hdts|hdcam|cam|ts|tc|r5|" +
        @"remux|proper|repack|extended|unrated|uncut|imax|" +
        @"aac(?:2\.0|5\.1)?|ac3|eac3|dts(?:[\-\.]hd)?|truehd|atmos|dd[p5][\.\s]?[01]|[257]\.1|" +
        @"multi|multisub|multisubs|dual|dual[\-\.]?audio|vostfr|vo|vf|vff|vfq|subs?|subbed|dubbed|hardsub|" +
        @"vip|adult|xxx|new|hot|exclusive|" +
        @"netflix|nf|amzn|amazon|disney\+?|dsnp|hulu|hbo|max|appletv\+?|atvp|paramount\+?|pmtp|peacock" +
        @")(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _emptyBrackets = new(
        @"[\[\(\{]\s*[\]\)\}]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        _regexTimeout);

    private static readonly Regex _collapseSeparators = new(
        @"[\s_\.]{2,}|\s*[-–]\s*[-–]\s*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        _regexTimeout);

    /// <summary>
    /// Splits a provider movie title into a clean title and a year.
    /// </summary>
    /// <param name="raw">The provider's title.</param>
    /// <returns>The parsed pieces.</returns>
    public static ParsedMovieTitle ParseMovie(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedMovieTitle(string.Empty, null, null);
        }

        var (body, tag) = StripLeadingTag(raw.Trim());
        var (withoutYear, year) = ExtractYear(body);
        var title = Clean(withoutYear);

        // Stripping noise can eat the whole title, for instance on an entry literally named "4K".
        if (title.Length == 0)
        {
            title = Clean(withoutYear, removeQualityNoise: false);
        }

        if (title.Length == 0)
        {
            title = raw.Trim();
        }

        return new ParsedMovieTitle(title, year, tag);
    }

    /// <summary>
    /// Splits a provider episode title into a series title, season, episode, and episode title.
    /// </summary>
    /// <param name="raw">The provider's title.</param>
    /// <returns>The parsed pieces, or <c>null</c> when the title holds no episode marker.</returns>
    public static ParsedEpisodeTitle? ParseEpisode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var (body, tag) = StripLeadingTag(raw.Trim());

        var match = Match(body);
        if (match is null)
        {
            return null;
        }

        var (marker, season, episode) = match.Value;

        var beforeMarker = body[..marker.Index];
        var afterMarker = body[(marker.Index + marker.Length)..];

        var (seriesWithoutYear, year) = ExtractYear(beforeMarker);
        var seriesTitle = Clean(seriesWithoutYear);
        if (seriesTitle.Length == 0)
        {
            seriesTitle = Clean(seriesWithoutYear, removeQualityNoise: false);
        }

        if (seriesTitle.Length == 0)
        {
            return null;
        }

        var episodeTitle = Clean(afterMarker);
        if (episodeTitle.Length == 0 || IsOnlyPunctuation(episodeTitle))
        {
            episodeTitle = null;
        }

        return new ParsedEpisodeTitle(seriesTitle, season, episode, episodeTitle, year, tag);
    }

    /// <summary>
    /// Splits a provider's comma, slash, or pipe separated list into trimmed entries.
    /// </summary>
    /// <param name="raw">The raw list.</param>
    /// <returns>The entries, with blanks and duplicates removed.</returns>
    public static IReadOnlyList<string> SplitList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        var normalized = Regex.Replace(raw, @"\bN\s*/\s*A\b", " ", RegexOptions.IgnoreCase, _regexTimeout);

        return normalized
            .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.Length > 0 && !string.Equals(part, "NA", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Parses the assorted date formats panels return.
    /// </summary>
    /// <param name="raw">The raw date.</param>
    /// <returns>The parsed date, or <c>null</c> when it is unusable.</returns>
    public static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();

        var formats = new[]
        {
            "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy", "dd/MM/yyyy", "MM/dd/yyyy",
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy"
        };

        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var loose))
        {
            return loose;
        }

        // Some panels send a Unix timestamp in a string field.
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 100_000_000)
        {
            return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        }

        return null;
    }

    private static (Match Marker, int Season, int Episode)? Match(string body)
    {
        foreach (var regex in new[] { _seasonEpisode, _wordyEpisode, _crossEpisode })
        {
            Match match;
            try
            {
                match = regex.Match(body);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!match.Success)
            {
                continue;
            }

            // `1x02` also matches aspect ratios and resolutions, so require a plausible season.
            var season = int.Parse(match.Groups["season"].Value, CultureInfo.InvariantCulture);
            var episode = int.Parse(match.Groups["episode"].Value, CultureInfo.InvariantCulture);
            if (regex == _crossEpisode && (season > 50 || episode > 500))
            {
                continue;
            }

            return (match, season, episode);
        }

        return null;
    }

    private static (string Body, string? Tag) StripLeadingTag(string value)
    {
        Match match;
        try
        {
            match = _leadingTag.Match(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return (value, null);
        }

        if (!match.Success)
        {
            return (value, null);
        }

        var remainder = value[match.Length..].Trim();

        // Guard against eating the real title, as in "UP" or "ET - The Extra Terrestrial".
        if (remainder.Length < 2)
        {
            return (value, null);
        }

        return (remainder, match.Groups["tag"].Value);
    }

    private static (string Body, int? Year) ExtractYear(string value)
    {
        try
        {
            var bracketed = _bracketedYear.Match(value);
            if (bracketed.Success)
            {
                var year = int.Parse(bracketed.Groups["year"].Value, CultureInfo.InvariantCulture);
                return (value.Remove(bracketed.Index, bracketed.Length), year);
            }

            var trailing = _trailingYear.Match(value.TrimEnd(' ', '-', '–', '.', '_'));
            if (trailing.Success)
            {
                var year = int.Parse(trailing.Groups["year"].Value, CultureInfo.InvariantCulture);
                return (value.Remove(trailing.Index, trailing.Length), year);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return (value, null);
        }

        return (value, null);
    }

    private static string Clean(string value, bool removeQualityNoise = true)
    {
        var text = value;

        try
        {
            if (removeQualityNoise)
            {
                text = _qualityNoise.Replace(text, " ");
            }

            text = _emptyBrackets.Replace(text, " ");
            text = _collapseSeparators.Replace(text, " ");
        }
        catch (RegexMatchTimeoutException)
        {
            text = value;
        }

        text = text.Replace('_', ' ');
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return text.Trim(' ', '-', '–', ':', '|', '.', ',', '[', ']', '(', ')', '{', '}');
    }

    private static bool IsOnlyPunctuation(string value)
        => value.All(c => !char.IsLetterOrDigit(c));
}
