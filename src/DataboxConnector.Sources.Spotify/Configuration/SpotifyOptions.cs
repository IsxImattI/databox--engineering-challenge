using System.ComponentModel.DataAnnotations;

namespace DataboxConnector.Sources.Spotify.Configuration;

/// <summary>
/// Configuration for the Spotify source.
/// </summary>
/// <remarks>
/// <para>
/// Bound from the <c>Sources:Spotify</c> configuration section. The
/// <see cref="ClientId"/> and <see cref="ClientSecret"/> are issued from
/// the Spotify developer dashboard.
/// </para>
/// <para>
/// Tokens (access + refresh) are NOT stored here — they live in a separate
/// file written by the one-time auth bootstrap CLI tool.
/// </para>
/// </remarks>
public sealed class SpotifyOptions
{
    public const string SectionName = "Sources:Spotify";

    /// <summary>Spotify Web API base URL.</summary>
    [Required]
    [Url]
    public string ApiBaseUrl { get; set; } = "https://api.spotify.com";

    /// <summary>Spotify accounts (auth) base URL.</summary>
    [Required]
    [Url]
    public string AccountsBaseUrl { get; set; } = "https://accounts.spotify.com";

    /// <summary>OAuth client ID from the Spotify developer dashboard.</summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth client secret from the Spotify developer dashboard.</summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI registered with the Spotify app. Must match the value
    /// used during the auth bootstrap CLI exactly.
    /// </summary>
    [Required]
    [Url]
    public string RedirectUri { get; set; } = "http://localhost:8888/callback";

    /// <summary>
    /// Path to the JSON file that stores the OAuth tokens.
    /// Relative paths are resolved against the host's content root.
    /// </summary>
    [Required]
    public string TokenStorePath { get; set; } = "data/spotify-tokens.json";

    /// <summary>
    /// Default number of days to look back when no <c>From</c> is supplied.
    /// Spotify's recently-played endpoint only returns the last 50 plays
    /// regardless of this value, but it bounds the watermark logic.
    /// </summary>
    [Range(1, 365)]
    public int DefaultLookbackDays { get; set; } = 7;

    /// <summary>
    /// Time ranges to extract for top tracks. Spotify exposes:
    /// <c>short_term</c> (~4 weeks), <c>medium_term</c> (~6 months),
    /// <c>long_term</c> (years).
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<string> TopTrackTimeRanges { get; set; } = new() { "short_term", "medium_term", "long_term" };

    /// <summary>
    /// Number of top tracks to fetch per time range. Spotify caps this at 50.
    /// </summary>
    [Range(1, 50)]
    public int TopTracksLimit { get; set; } = 20;
}