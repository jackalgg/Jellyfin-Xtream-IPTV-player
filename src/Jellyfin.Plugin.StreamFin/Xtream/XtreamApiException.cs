using System;

namespace Jellyfin.Plugin.StreamFin.Xtream;

/// <summary>
/// Raised when a provider's API cannot be used, with a message meant for the settings page.
/// </summary>
public class XtreamApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="XtreamApiException"/> class.
    /// </summary>
    public XtreamApiException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XtreamApiException"/> class.
    /// </summary>
    /// <param name="message">A message describing what the provider did wrong.</param>
    public XtreamApiException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XtreamApiException"/> class.
    /// </summary>
    /// <param name="message">A message describing what the provider did wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public XtreamApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
