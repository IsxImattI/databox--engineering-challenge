using DataboxConnector.Sources.Spotify.OAuth;
using FluentAssertions;
using Xunit;

namespace DataboxConnector.Sources.Spotify.Tests.OAuth;

public class SpotifyTokensTests
{
    [Fact]
    public void IsAccessTokenExpired_TokenExpiringInOneHour_NotExpiredWithinSixtySecondMargin()
    {
        var tokens = new SpotifyTokens
        {
            AccessToken = "a",
            RefreshToken = "r",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        tokens.IsAccessTokenExpired(TimeSpan.FromSeconds(60)).Should().BeFalse();
    }

    [Fact]
    public void IsAccessTokenExpired_TokenExpiringInThirtySeconds_ExpiredWithinSixtySecondMargin()
    {
        var tokens = new SpotifyTokens
        {
            AccessToken = "a",
            RefreshToken = "r",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30)
        };

        tokens.IsAccessTokenExpired(TimeSpan.FromSeconds(60)).Should().BeTrue();
    }

    [Fact]
    public void IsAccessTokenExpired_AlreadyExpired_True()
    {
        var tokens = new SpotifyTokens
        {
            AccessToken = "a",
            RefreshToken = "r",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        tokens.IsAccessTokenExpired(TimeSpan.Zero).Should().BeTrue();
    }
}