using DataboxConnector.Core.Schema;

namespace DataboxConnector.Sources.Spotify.Schema;

/// <summary>
/// Schema for the <c>spotify_top_tracks_v1</c> dataset.
/// </summary>
/// <remarks>
/// Snapshot-style: each run captures the user's top tracks as of <c>captured_at</c>.
/// Time-series of these snapshots powers "how my taste evolves" type metrics.
/// </remarks>
public static class SpotifyTopTracksSchema
{
    public const string Key = "spotify_top_tracks_v1";
    public const string Title = "Spotify Top Tracks";

    public static DatasetSchema Instance { get; } = new(
        Key,
        Title,
        new[]
        {
            new FieldDefinition
            {
                Name = "captured_at",
                Type = FieldType.DateTime,
                IsPrimaryKey = true,
                Description = "Snapshot timestamp (UTC)."
            },
            new FieldDefinition
            {
                Name = "time_range",
                Type = FieldType.String,
                IsPrimaryKey = true,
                Description = "short_term (~4 weeks), medium_term (~6 months), long_term (years)."
            },
            new FieldDefinition
            {
                Name = "rank",
                Type = FieldType.Integer,
                IsPrimaryKey = true,
                Description = "Position within the snapshot, 1-based."
            },
            new FieldDefinition
            {
                Name = "track_id",
                Type = FieldType.String,
                Description = "Spotify track id."
            },
            new FieldDefinition
            {
                Name = "track_name",
                Type = FieldType.String,
                Description = "Track display name."
            },
            new FieldDefinition
            {
                Name = "artist_name",
                Type = FieldType.String,
                Description = "Primary artist."
            },
            new FieldDefinition
            {
                Name = "popularity",
                Type = FieldType.Integer,
                Description = "Spotify popularity score (0-100)."
            }
        });
}