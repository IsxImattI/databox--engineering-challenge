using DataboxConnector.Core.Models;
using DataboxConnector.Sources.Spotify.Models;

namespace DataboxConnector.Sources.Spotify.Mapping;

/// <summary>
/// Maps Spotify API DTOs to <see cref="RawRecord"/> instances conforming
/// to the relevant dataset schema.
/// </summary>
internal static class SpotifyMappers
{
    /// <summary>
    /// Maps a play history item to a <c>spotify_recently_played_v1</c> record.
    /// Returns null if required fields are missing.
    /// </summary>
    public static RawRecord? MapPlayHistoryItem(SpotifyPlayHistoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var track = item.Track;
        if (track is null || string.IsNullOrEmpty(track.Id) || string.IsNullOrEmpty(track.Name))
            return null;

        var firstArtist = track.Artists?.FirstOrDefault()?.Name;
        var albumName = track.Album?.Name;
        if (firstArtist is null || albumName is null)
            return null;

        var fields = new Dictionary<string, object?>
        {
            ["played_at"]  = item.PlayedAt.ToUniversalTime(),
            ["track_id"]   = track.Id,
            ["track_name"] = track.Name,
            ["artist_name"] = firstArtist,
            ["album_name"]  = albumName,
            ["duration_ms"] = track.DurationMs,
            ["popularity"]  = track.Popularity,
            ["explicit"]    = track.Explicit
        };

        return RawRecord.From(fields);
    }

    /// <summary>
    /// Maps a top track at <paramref name="rank"/> within
    /// <paramref name="timeRange"/> to a <c>spotify_top_tracks_v1</c> record.
    /// </summary>
    public static RawRecord? MapTopTrack(
        SpotifyTrack track,
        string timeRange,
        int rank,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeRange);

        if (string.IsNullOrEmpty(track.Id) || string.IsNullOrEmpty(track.Name))
            return null;

        var firstArtist = track.Artists?.FirstOrDefault()?.Name;
        if (firstArtist is null)
            return null;

        var fields = new Dictionary<string, object?>
        {
            ["captured_at"] = capturedAt.UtcDateTime,
            ["time_range"]  = timeRange,
            ["rank"]        = rank,
            ["track_id"]    = track.Id,
            ["track_name"]  = track.Name,
            ["artist_name"] = firstArtist,
            ["popularity"]  = track.Popularity
        };

        return RawRecord.From(fields);
    }
}