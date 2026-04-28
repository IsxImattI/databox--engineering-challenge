using DataboxConnector.Core.Schema;

namespace DataboxConnector.Sources.Spotify.Schema;

/// <summary>
/// Schema for the <c>spotify_recently_played_v1</c> dataset.
/// </summary>
public static class SpotifyRecentlyPlayedSchema
{
    public const string Key = "spotify_recently_played_v1";
    public const string Title = "Spotify Recently Played";

    public static DatasetSchema Instance { get; } = new(
        Key,
        Title,
        new[]
        {
            new FieldDefinition
            {
                Name = "played_at",
                Type = FieldType.DateTime,
                IsPrimaryKey = true,
                Description = "When the track was played (UTC)."
            },
            new FieldDefinition
            {
                Name = "track_id",
                Type = FieldType.String,
                IsPrimaryKey = true,
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
                Description = "Primary artist (first listed)."
            },
            new FieldDefinition
            {
                Name = "album_name",
                Type = FieldType.String,
                Description = "Album the track appears on."
            },
            new FieldDefinition
            {
                Name = "duration_ms",
                Type = FieldType.Integer,
                Description = "Track duration in milliseconds."
            },
            new FieldDefinition
            {
                Name = "popularity",
                Type = FieldType.Integer,
                Description = "Spotify popularity score (0-100)."
            },
            new FieldDefinition
            {
                Name = "explicit",
                Type = FieldType.Boolean,
                Description = "Whether the track is marked explicit."
            }
        });
}