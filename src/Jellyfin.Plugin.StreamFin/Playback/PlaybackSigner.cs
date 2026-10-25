using System;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.StreamFin.Playback;

/// <summary>
/// Signs and verifies StreamFin playback URLs so the anonymous play endpoint cannot be used as an
/// open proxy.
/// </summary>
public static class PlaybackSigner
{
    /// <summary>
    /// Builds the canonical payload that is signed for one playback request.
    /// </summary>
    /// <param name="kind">The stream kind.</param>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="streamId">The stream identifier within the provider.</param>
    /// <param name="container">The preferred container extension.</param>
    /// <param name="upstreamUrl">The original upstream address, when one is known.</param>
    /// <returns>The canonical payload.</returns>
    public static string BuildPayload(
        PlaybackKind kind,
        string providerId,
        string streamId,
        string? container,
        string? upstreamUrl)
    {
        return string.Join(
            '\n',
            kind.ToString().ToLowerInvariant(),
            providerId ?? string.Empty,
            streamId ?? string.Empty,
            (container ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant(),
            upstreamUrl ?? string.Empty);
    }

    /// <summary>
    /// Signs a payload with HMAC-SHA256 and returns a lowercase hex digest.
    /// </summary>
    /// <param name="key">The signing key.</param>
    /// <param name="payload">The payload to sign.</param>
    /// <returns>The hex digest.</returns>
    public static string Sign(ReadOnlySpan<byte> key, string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies a hex digest against a payload.
    /// </summary>
    /// <param name="key">The signing key.</param>
    /// <param name="payload">The payload that was signed.</param>
    /// <param name="signature">The hex digest to check.</param>
    /// <returns><c>true</c> when the signature matches.</returns>
    public static bool Verify(ReadOnlySpan<byte> key, string payload, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || signature.Length != 64)
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
