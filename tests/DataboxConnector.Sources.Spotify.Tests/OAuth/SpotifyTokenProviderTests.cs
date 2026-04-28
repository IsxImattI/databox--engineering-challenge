using System.Net;
using System.Net.Http;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Sources.Spotify.Configuration;
using DataboxConnector.Sources.Spotify.OAuth;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;
using Xunit;

namespace DataboxConnector.Sources.Spotify.Tests.OAuth;

public class SpotifyTokenProviderTests
{
    private static readonly Uri AccountsUri = new("https://accounts.spotify.test");

    private static IOptions<SpotifyOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new SpotifyOptions
        {
            ClientId = "client-1",
            ClientSecret = "secret-1",
            AccountsBaseUrl = AccountsUri.ToString().TrimEnd('/')
        });

    private static (SpotifyTokenProvider provider, MockHttpMessageHandler mock, FakeTokenStore store) NewProvider(
        SpotifyTokens? initial = null)
    {
        var mock = new MockHttpMessageHandler();
        var http = new HttpClient(mock) { BaseAddress = AccountsUri };
        var store = new FakeTokenStore(initial);
        var provider = new SpotifyTokenProvider(
            store: store,
            options: Options(),
            http: http,
            logger: NullLogger<SpotifyTokenProvider>.Instance);
        return (provider, mock, store);
    }

    private static SpotifyTokens FreshTokens() => new()
    {
        AccessToken = "access-fresh",
        RefreshToken = "refresh-1",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
    };

    private static SpotifyTokens ExpiredTokens() => new()
    {
        AccessToken = "access-old",
        RefreshToken = "refresh-1",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    [Fact]
    public async Task GetAccessTokenAsync_NoStoredTokens_Throws()
    {
        var (provider, _, _) = NewProvider(initial: null);

        var act = async () => await provider.GetAccessTokenAsync();

        await act.Should().ThrowAsync<SourceExtractionException>()
            .WithMessage("*bootstrap*");
    }

    [Fact]
    public async Task GetAccessTokenAsync_FreshTokens_ReturnsCachedAccessToken()
    {
        var (provider, mock, _) = NewProvider(initial: FreshTokens());

        var token = await provider.GetAccessTokenAsync();

        token.Should().Be("access-fresh");
        mock.GetMatchCount(mock.Expect(HttpMethod.Post, "*/api/token")).Should().Be(0);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ExpiredTokens_RefreshesAndPersists()
    {
        var (provider, mock, store) = NewProvider(initial: ExpiredTokens());

        mock.When(HttpMethod.Post, AccountsUri + "api/token")
            .Respond("application/json", """
                {
                    "access_token": "access-new",
                    "token_type": "Bearer",
                    "expires_in": 3600,
                    "scope": "user-read-recently-played"
                }
                """);

        var token = await provider.GetAccessTokenAsync();

        token.Should().Be("access-new");
        store.Saved.Should().NotBeNull();
        store.Saved!.AccessToken.Should().Be("access-new");
        store.Saved.RefreshToken.Should().Be("refresh-1", "refresh token preserved when not in response");
    }

    [Fact]
    public async Task GetAccessTokenAsync_RefreshReturnsNewRefreshToken_PersistsIt()
    {
        var (provider, mock, store) = NewProvider(initial: ExpiredTokens());

        mock.When(HttpMethod.Post, AccountsUri + "api/token")
            .Respond("application/json", """
                {
                    "access_token": "access-new",
                    "refresh_token": "refresh-rotated",
                    "expires_in": 3600
                }
                """);

        await provider.GetAccessTokenAsync();

        store.Saved!.RefreshToken.Should().Be("refresh-rotated");
    }

    [Fact]
    public async Task GetAccessTokenAsync_RefreshFails_Throws()
    {
        var (provider, mock, _) = NewProvider(initial: ExpiredTokens());

        mock.When(HttpMethod.Post, AccountsUri + "api/token")
            .Respond(HttpStatusCode.BadRequest, "application/json",
                """{"error": "invalid_grant"}""");

        var act = async () => await provider.GetAccessTokenAsync();

        var ex = await act.Should().ThrowAsync<SourceExtractionException>();
        ex.Which.Message.Should().Contain("400");
    }

    [Fact]
    public async Task GetAccessTokenAsync_ConcurrentExpired_OnlyRefreshesOnce()
    {
        var (provider, mock, store) = NewProvider(initial: ExpiredTokens());

        var refreshCount = 0;
        mock.When(HttpMethod.Post, AccountsUri + "api/token")
            .Respond(req =>
            {
                Interlocked.Increment(ref refreshCount);
                Thread.Sleep(50); // simulate latency so concurrent calls actually overlap
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"access-new","expires_in":3600}""",
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
            });

        var tasks = Enumerable.Range(0, 10).Select(_ => provider.GetAccessTokenAsync()).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Should().AllBe("access-new");
        refreshCount.Should().Be(1, "double-checked locking should ensure only one refresh");
    }

    /// <summary>
    /// Test double for ISpotifyTokenStore that holds tokens in memory.
    /// </summary>
    private sealed class FakeTokenStore : ISpotifyTokenStore
    {
        private SpotifyTokens? _tokens;
        public SpotifyTokens? Saved => _tokens;

        public FakeTokenStore(SpotifyTokens? initial) => _tokens = initial;

        public Task<SpotifyTokens?> LoadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_tokens);

        public Task SaveAsync(SpotifyTokens tokens, CancellationToken cancellationToken = default)
        {
            _tokens = tokens;
            return Task.CompletedTask;
        }
    }
}