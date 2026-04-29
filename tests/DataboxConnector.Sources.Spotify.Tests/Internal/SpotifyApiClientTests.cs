using System.Net;
using System.Net.Http;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Sources.Spotify.Internal;
using DataboxConnector.Sources.Spotify.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Xunit;
using DataboxConnector.Sources.Spotify.OAuth;

namespace DataboxConnector.Sources.Spotify.Tests.Internal;

public class SpotifyApiClientTests
{
    private static readonly Uri ApiUri = new("https://api.spotify.test");

    private static (SpotifyApiClient client, MockHttpMessageHandler mock) NewClient()
    {
        var mock = new MockHttpMessageHandler();
        var http = new HttpClient(mock) { BaseAddress = ApiUri };
        var tokenProvider = new FakeTokenProvider();
        return (
            new SpotifyApiClient(http, tokenProvider, NullLogger<SpotifyApiClient>.Instance),
            mock);
    }

    /// <summary>
    /// Test fake for ISpotifyTokenProvider that returns a static token without
    /// touching the network.
    /// </summary>
    private sealed class FakeTokenProvider : DataboxConnector.Sources.Spotify.OAuth.ISpotifyTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("fake-test-token");
    }

    [Fact]
    public async Task GetRecentlyPlayedAsync_Success_ReturnsItems()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, ApiUri + "v1/me/player/recently-played*")
            .Respond("application/json", """
                {
                    "items": [
                        {
                            "track": { "id": "t1", "name": "Track1",
                                       "artists": [{"name":"A1"}],
                                       "album": {"name":"Album1"},
                                       "duration_ms": 180000,
                                       "popularity": 50,
                                       "explicit": false },
                            "played_at": "2026-04-01T10:00:00Z"
                        }
                    ]
                }
                """);

        var items = await client.GetRecentlyPlayedAsync(DateTimeOffset.UtcNow.AddDays(-1));

        items.Should().HaveCount(1);
        items[0].Track!.Id.Should().Be("t1");
    }

    [Fact]
    public async Task GetRecentlyPlayedAsync_401_ThrowsWithReauthMessage()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, ApiUri + "v1/me/player/recently-played*")
            .Respond(HttpStatusCode.Unauthorized, "application/json", """{"error":"invalid_token"}""");

        var act = async () => await client.GetRecentlyPlayedAsync(DateTimeOffset.UtcNow.AddDays(-1));

        var ex = await act.Should().ThrowAsync<SourceExtractionException>();
        ex.Which.Message.Should().Contain("bootstrap");
    }

    [Fact]
    public async Task GetTopTracksAsync_Success_ReturnsItems()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, ApiUri + "v1/me/top/tracks*")
            .Respond("application/json", """
                {
                    "items": [
                        { "id": "t1", "name": "Hit",
                          "artists": [{"name":"Star"}],
                          "album": {"name":"Best"},
                          "duration_ms": 200000,
                          "popularity": 90,
                          "explicit": true }
                    ]
                }
                """);

        var items = await client.GetTopTracksAsync("short_term", 10);

        items.Should().HaveCount(1);
        items[0].Id.Should().Be("t1");
        items[0].Popularity.Should().Be(90);
    }

    [Fact]
    public async Task GetTopTracksAsync_LimitOutOfRange_Throws()
    {
        var (client, _) = NewClient();

        var act = async () => await client.GetTopTracksAsync("short_term", 0);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetTopTracksAsync_LimitTooHigh_Throws()
    {
        var (client, _) = NewClient();

        var act = async () => await client.GetTopTracksAsync("short_term", 51);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetTopTracksAsync_429_ThrowsRateLimitMessage()
    {
        var (client, mock) = NewClient();

        mock.When(HttpMethod.Get, ApiUri + "v1/me/top/tracks*")
            .Respond(req =>
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
                resp.Content = new StringContent("rate limited");
                return resp;
            });

        var act = async () => await client.GetTopTracksAsync("short_term", 10);

        var ex = await act.Should().ThrowAsync<SourceExtractionException>();
        ex.Which.Message.Should().Contain("rate-limited");
    }
}