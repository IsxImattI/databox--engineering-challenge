using System.Net.Http.Json;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Sources.Spotify.Models;
using Microsoft.Extensions.Logging;

namespace DataboxConnector.Sources.Spotify.Internal;

/// <summary>
/// HTTP-backed implementation of <see cref="ISpotifyApiClient"/>.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> is preconfigured with the Spotify API base
/// URL and runs through <see cref="SpotifyAuthHandler"/>, which attaches
/// the Bearer token automatically.
/// </remarks>
internal sealed class SpotifyApiClient : ISpotifyApiClient
{
    private const string SourceName = "spotify";

    private readonly HttpClient _http;
    private readonly ILogger<SpotifyApiClient> _logger;

    public SpotifyApiClient(HttpClient http, ILogger<SpotifyApiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
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

        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
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

        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, $"top-tracks/{timeRange}", cancellationToken).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<SpotifyTopTracksResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return payload?.Items ?? new List<SpotifyTrack>();
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
        value.Length <= max ? value : value[..max] + "…";
}