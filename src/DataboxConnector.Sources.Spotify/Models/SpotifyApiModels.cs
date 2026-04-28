using System.Text.Json.Serialization;

namespace DataboxConnector.Sources.Spotify.Models;

internal sealed class SpotifyTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

internal sealed class SpotifyRecentlyPlayedResponse
{
    [JsonPropertyName("items")]
    public List<SpotifyPlayHistoryItem>? Items { get; set; }

    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("cursors")]
    public SpotifyCursors? Cursors { get; set; }
}

internal sealed class SpotifyCursors
{
    [JsonPropertyName("after")]
    public string? After { get; set; }

    [JsonPropertyName("before")]
    public string? Before { get; set; }
}

internal sealed class SpotifyPlayHistoryItem
{
    [JsonPropertyName("track")]
    public SpotifyTrack? Track { get; set; }

    [JsonPropertyName("played_at")]
    public DateTime PlayedAt { get; set; }
}

internal sealed class SpotifyTopTracksResponse
{
    [JsonPropertyName("items")]
    public List<SpotifyTrack>? Items { get; set; }
}

internal sealed class SpotifyTrack
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("artists")]
    public List<SpotifyArtist>? Artists { get; set; }

    [JsonPropertyName("album")]
    public SpotifyAlbum? Album { get; set; }

    [JsonPropertyName("duration_ms")]
    public int DurationMs { get; set; }

    [JsonPropertyName("popularity")]
    public int Popularity { get; set; }

    [JsonPropertyName("explicit")]
    public bool Explicit { get; set; }
}

internal sealed class SpotifyArtist
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class SpotifyAlbum
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}