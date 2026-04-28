namespace DataboxConnector.Sources.Spotify.OAuth;

/// <summary>
/// Spotify OAuth tokens persisted to <c>tokens.json</c> by the auth bootstrap
/// tool and read by the running connector.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AccessToken"/> typically expires one hour after issuance.
/// <see cref="RefreshToken"/> is long-lived and is used to obtain new access
/// tokens without prompting the user again.
/// </para>
/// </remarks>
public sealed record SpotifyTokens
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset AccessTokenExpiresAt { get; init; }

    /// <summary>
    /// Returns <c>true</c> when the access token is within
    /// <paramref name="margin"/> of expiring (or already expired).
    /// </summary>
    public bool IsAccessTokenExpired(TimeSpan margin) =>
        DateTimeOffset.UtcNow + margin >= AccessTokenExpiresAt;
}