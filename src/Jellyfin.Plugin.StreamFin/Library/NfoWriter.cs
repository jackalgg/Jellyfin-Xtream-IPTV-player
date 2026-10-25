using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Jellyfin.Plugin.StreamFin.Catalog;

namespace Jellyfin.Plugin.StreamFin.Library;

/// <summary>
/// Writes Kodi-style <c>.nfo</c> sidecars.
/// </summary>
/// <remarks>
/// Jellyfin reads NFO sidecars with its built-in local metadata provider, so writing the
/// provider's own titles, plots, artwork URLs, and TMDB identifiers next to each <c>.strm</c>
/// means a freshly synced library is browsable immediately instead of sitting blank while
/// thousands of items queue for an internet metadata lookup. Where a TMDB identifier is present,
/// Jellyfin's own providers can still fill in the rest exactly.
/// </remarks>
public static class NfoWriter
{
    private static readonly XmlWriterSettings _settings = new()
    {
        Indent = true,
        IndentChars = "  ",
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        OmitXmlDeclaration = false,
        CloseOutput = false,
        NewLineChars = "\n"
    };

    /// <summary>
    /// Renders a movie's NFO.
    /// </summary>
    /// <param name="movie">The movie.</param>
    /// <returns>The NFO document.</returns>
    public static string BuildMovie(CatalogMovie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);

        return Build("movie", writer =>
        {
            WriteTitle(writer, movie);
            WriteCommon(writer, movie);

            if (movie.Runtime is { TotalMinutes: > 0 })
            {
                writer.WriteElementString("runtime", ((int)movie.Runtime.Value.TotalMinutes).ToString(CultureInfo.InvariantCulture));
            }
        });
    }

    /// <summary>
    /// Renders a series' NFO.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <returns>The NFO document.</returns>
    public static string BuildSeries(CatalogSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return Build("tvshow", writer =>
        {
            WriteTitle(writer, series);
            WriteCommon(writer, series);

            if (series.EpisodeRuntime is { TotalMinutes: > 0 })
            {
                writer.WriteElementString("runtime", ((int)series.EpisodeRuntime.Value.TotalMinutes).ToString(CultureInfo.InvariantCulture));
            }
        });
    }

    /// <summary>
    /// Renders a season's NFO.
    /// </summary>
    /// <param name="season">The season.</param>
    /// <param name="seriesTitle">The owning series' title.</param>
    /// <returns>The NFO document.</returns>
    public static string BuildSeason(CatalogSeason season, string seriesTitle)
    {
        ArgumentNullException.ThrowIfNull(season);

        return Build("season", writer =>
        {
            var name = string.IsNullOrWhiteSpace(season.Name)
                ? (season.Number <= 0 ? "Specials" : string.Create(CultureInfo.InvariantCulture, $"Season {season.Number}"))
                : season.Name!;

            writer.WriteElementString("title", name);
            writer.WriteElementString("showtitle", seriesTitle);
            writer.WriteElementString("seasonnumber", season.Number.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(season.Overview))
            {
                writer.WriteElementString("plot", season.Overview);
            }

            if (season.AirDate is not null)
            {
                writer.WriteElementString("premiered", season.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            WriteArtwork(writer, season.PosterUrl, Array.Empty<string>());
        });
    }

    /// <summary>
    /// Renders an episode's NFO.
    /// </summary>
    /// <param name="episode">The episode.</param>
    /// <param name="series">The owning series.</param>
    /// <returns>The NFO document.</returns>
    public static string BuildEpisode(CatalogEpisode episode, CatalogSeries series)
    {
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(series);

        return Build("episodedetails", writer =>
        {
            var title = string.IsNullOrWhiteSpace(episode.Title)
                ? string.Create(CultureInfo.InvariantCulture, $"Episode {episode.EpisodeNumber}")
                : episode.Title!;

            writer.WriteElementString("title", title);
            writer.WriteElementString("showtitle", series.Title);
            writer.WriteElementString("season", episode.SeasonNumber.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("episode", episode.EpisodeNumber.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(episode.Plot))
            {
                writer.WriteElementString("plot", episode.Plot);
            }

            if (episode.Rating is > 0)
            {
                writer.WriteElementString("rating", episode.Rating.Value.ToString("0.0", CultureInfo.InvariantCulture));
            }

            if (episode.AirDate is not null)
            {
                writer.WriteElementString("aired", episode.AirDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            if (episode.Runtime is { TotalMinutes: > 0 })
            {
                writer.WriteElementString("runtime", ((int)episode.Runtime.Value.TotalMinutes).ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(episode.TmdbId))
            {
                writer.WriteStartElement("uniqueid");
                writer.WriteAttributeString("type", "tmdb");
                writer.WriteString(episode.TmdbId);
                writer.WriteEndElement();
            }

            if (!string.IsNullOrWhiteSpace(episode.ImageUrl))
            {
                writer.WriteStartElement("thumb");
                writer.WriteAttributeString("aspect", "thumb");
                writer.WriteString(episode.ImageUrl);
                writer.WriteEndElement();
            }
        });
    }

    private static void WriteTitle(XmlWriter writer, CatalogTitle title)
    {
        writer.WriteElementString("title", title.Title);

        if (!string.IsNullOrWhiteSpace(title.OriginalTitle)
            && !string.Equals(title.OriginalTitle, title.Title, StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteElementString("originaltitle", title.OriginalTitle);
        }
    }

    private static void WriteCommon(XmlWriter writer, CatalogTitle title)
    {
        if (!string.IsNullOrWhiteSpace(title.Plot))
        {
            writer.WriteElementString("plot", title.Plot);
        }

        if (title.Year is > 0)
        {
            writer.WriteElementString("year", title.Year.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (title.ReleaseDate is not null)
        {
            writer.WriteElementString("premiered", title.ReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (title.Rating is > 0)
        {
            writer.WriteElementString("rating", title.Rating.Value.ToString("0.0", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(title.Certification))
        {
            writer.WriteElementString("mpaa", title.Certification);
        }

        foreach (var genre in title.Genres)
        {
            writer.WriteElementString("genre", genre);
        }

        foreach (var director in title.Directors)
        {
            writer.WriteElementString("director", director);
        }

        foreach (var actor in title.Cast)
        {
            writer.WriteStartElement("actor");
            writer.WriteElementString("name", actor);
            writer.WriteEndElement();
        }

        if (!string.IsNullOrWhiteSpace(title.TrailerUrl))
        {
            writer.WriteElementString("trailer", title.TrailerUrl);
        }

        if (!string.IsNullOrWhiteSpace(title.TmdbId))
        {
            writer.WriteStartElement("uniqueid");
            writer.WriteAttributeString("type", "tmdb");
            writer.WriteAttributeString("default", "true");
            writer.WriteString(title.TmdbId);
            writer.WriteEndElement();
        }

        WriteArtwork(writer, title.PosterUrl, title.BackdropUrls);
    }

    private static void WriteArtwork(XmlWriter writer, string? posterUrl, IReadOnlyList<string> backdropUrls)
    {
        if (!string.IsNullOrWhiteSpace(posterUrl))
        {
            writer.WriteStartElement("thumb");
            writer.WriteAttributeString("aspect", "poster");
            writer.WriteString(posterUrl);
            writer.WriteEndElement();
        }

        if (backdropUrls.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("fanart");
        foreach (var backdrop in backdropUrls)
        {
            if (string.IsNullOrWhiteSpace(backdrop))
            {
                continue;
            }

            writer.WriteStartElement("thumb");
            writer.WriteString(backdrop);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string Build(string rootElement, Action<XmlWriter> writeBody)
    {
        using var buffer = new MemoryStream();

        using (var writer = XmlWriter.Create(buffer, _settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement(rootElement);
            writeBody(writer);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
