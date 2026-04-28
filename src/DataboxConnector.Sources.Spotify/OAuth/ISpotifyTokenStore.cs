namespace DataboxConnector.Sources.Spotify.OAuth;

/// <summary>
/// Persists Spotify OAuth tokens between runs.
/// </summary>
/// <remarks>
/// The default file-based implementation is sufficient for local development.
/// In production, this would be replaced with a managed secret store
/// (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) transparently.
/// </remarks>
public interface ISpotifyTokenStore
{
    /// <summary>
    /// Loads the current tokens, or <c>null</c> if none have been written yet.
    /// </summary>
    Task<SpotifyTokens?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves (overwrites) the current tokens.
    /// </summary>
    Task SaveAsync(SpotifyTokens tokens, CancellationToken cancellationToken = default);
}