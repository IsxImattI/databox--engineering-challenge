namespace DataboxConnector.Sources.Spotify.OAuth;

/// <summary>
/// Provides a valid (non-expired) Spotify access token, refreshing it as needed.
/// </summary>
public interface ISpotifyTokenProvider
{
    /// <summary>
    /// Returns a valid access token. Refreshes the token transparently if
    /// the current one is expired or close to expiring.
    /// </summary>
    /// <exception cref="Core.Exceptions.SourceExtractionException">
    /// Thrown when the refresh flow fails (e.g. revoked refresh token).
    /// </exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}