using DataboxConnector.Sources.Spotify.Models;

namespace DataboxConnector.Sources.Spotify.Internal;

/// <summary>
/// Wrapper around the Spotify Web API endpoints used for ingestion.
/// </summary>
internal interface ISpotifyApiClient
{
    /// <summary>
    /// Fetches recently played tracks (Spotify caps at 50 most recent).
    /// </summary>
    Task<IReadOnlyList<SpotifyPlayHistoryItem>> GetRecentlyPlayedAsync(
        DateTimeOffset after,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the user's top tracks for the given time range.
    /// </summary>
    /// <param name="timeRange">short_term, medium_term, or long_term.</param>
    /// <param name="limit">Number of tracks to return (1-50).</param>
    Task<IReadOnlyList<SpotifyTrack>> GetTopTracksAsync(
        string timeRange,
        int limit,
        CancellationToken cancellationToken = default);
}