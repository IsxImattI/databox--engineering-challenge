using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Sources.Spotify.Configuration;
using DataboxConnector.Sources.Spotify.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sources.Spotify.OAuth;

/// <summary>
/// Default <see cref="ISpotifyTokenProvider"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// Loads tokens from <see cref="ISpotifyTokenStore"/> on first use, refreshes
/// them when expiry is within <see cref="RefreshMargin"/>, and persists the
/// refreshed tokens back to the store.
/// </para>
/// <para>
/// Concurrent refresh requests are serialized through a <see cref="SemaphoreSlim"/>
/// to avoid hitting Spotify with multiple parallel refresh calls.
/// </para>
/// </remarks>
internal sealed class SpotifyTokenProvider : ISpotifyTokenProvider
{
    private const string SourceName = "spotify";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly ISpotifyTokenStore _store;
    private readonly HttpClient _http;
    private readonly SpotifyOptions _options;
    private readonly ILogger<SpotifyTokenProvider> _logger;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private SpotifyTokens? _cached;

    public SpotifyTokenProvider(
        ISpotifyTokenStore store,
        HttpClient http,
        IOptions<SpotifyOptions> options,
        ILogger<SpotifyTokenProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokens = _cached ?? await _store.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new SourceExtractionException(SourceName,
                "No Spotify tokens found. Run the SpotifyAuth bootstrap CLI first.");

        if (!tokens.IsAccessTokenExpired(RefreshMargin))
        {
            _cached = tokens;
            return tokens.AccessToken;
        }

        // Token is expiring or expired — refresh under a lock to avoid races.
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock; another thread may have refreshed.
            tokens = _cached ?? await _store.LoadAsync(cancellationToken).ConfigureAwait(false) ?? tokens;
            if (!tokens.IsAccessTokenExpired(RefreshMargin))
            {
                _cached = tokens;
                return tokens.AccessToken;
            }

            var refreshed = await RefreshAsync(tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
            await _store.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
            _cached = refreshed;
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<SpotifyTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Refreshing Spotify access token.");

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/token")
        {
            Content = formContent
        };

        // Spotify accepts client credentials in the Basic auth header for refresh.
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new SourceExtractionException(SourceName,
                $"Spotify token refresh failed (HTTP {(int)response.StatusCode}): {body}");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<SpotifyTokenResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (payload?.AccessToken is null)
            throw new SourceExtractionException(SourceName,
                "Spotify token refresh returned no access token.");

        // Spotify only returns a new refresh_token sometimes; reuse the old one if not.
        return new SpotifyTokens
        {
            AccessToken = payload.AccessToken,
            RefreshToken = payload.RefreshToken ?? refreshToken,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn)
        };
    }
}