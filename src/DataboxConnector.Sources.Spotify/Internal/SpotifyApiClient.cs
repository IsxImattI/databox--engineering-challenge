using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Sources.Spotify.Models;
using DataboxConnector.Sources.Spotify.OAuth;
using Microsoft.Extensions.Logging;

namespace DataboxConnector.Sources.Spotify.Internal;

/// <summary>
/// HTTP-backed implementation of <see cref="ISpotifyApiClient"/>.
/// </summary>
/// <remarks>
/// Each request fetches a fresh access token from the
/// <see cref="ISpotifyTokenProvider"/>, which caches and refreshes transparently.
/// The token is attached as a Bearer Authorization header on the per-request
/// message rather than the shared HttpClient defaults, since the value can
/// change after a refresh.
/// </remarks>
internal sealed class SpotifyApiClient : ISpotifyApiClient
{
    private const string SourceName = "spotify";

    private readonly HttpClient _http;
    private readonly ISpotifyTokenProvider _tokenProvider;
    private readonly ILogger<SpotifyApiClient> _logger;

    public SpotifyApiClient(
        HttpClient http,
        ISpotifyTokenProvider tokenProvider,
        ILogger<SpotifyApiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SpotifyPlayHistoryItem>> GetRecentlyPlayedAsync(
        DateTimeOffset after,
        CancellationToken cancellationToken = default)
    {
        // Spotify's "after" parameter is a Unix timestamp in milliseconds.
        var afterMs = after.ToUnixTimeMilliseconds();
        var url = $"/v1/me/player/recently-played?after={afterMs}&limit=50";

        _logger.LogDebug("Fetching Spotify recently played after {After}.", after);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AttachAuthAsync(request, cancellationToken).ConfigureAwait(false);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "recently-played", cancellationToken).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<SpotifyRecentlyPlayedResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return payload?.Items ?? new List<SpotifyPlayHistoryItem>();
    }

    public async Task<IReadOnlyList<SpotifyTrack>> GetTopTracksAsync(
        string timeRange,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeRange);
        if (limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(limit), "Spotify caps top tracks at 50.");

        var url = $"/v1/me/top/tracks?time_range={Uri.EscapeDataString(timeRange)}&limit={limit}";

        _logger.LogDebug("Fetching Spotify top tracks: range={Range} limit={Limit}.", timeRange, limit);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AttachAuthAsync(request, cancellationToken).ConfigureAwait(false);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"top-tracks/{timeRange}", cancellationToken).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<SpotifyTopTracksResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return payload?.Items ?? new List<SpotifyTrack>();
    }

    private async Task AttachAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new SourceExtractionException(SourceName,
                $"Spotify {operation} returned 401. Tokens may be revoked; re-run the SpotifyAuth bootstrap CLI.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            throw new SourceExtractionException(SourceName,
                $"Spotify {operation} returned 429 (rate-limited). Retry-After: {retryAfter ?? 0}s.");
        }

        throw new SourceExtractionException(SourceName,
            $"Spotify {operation} failed with HTTP {statusCode}: {Truncate(body, 500)}");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}