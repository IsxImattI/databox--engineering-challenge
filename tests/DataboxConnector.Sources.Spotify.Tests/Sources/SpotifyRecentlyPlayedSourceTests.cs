using DataboxConnector.Core.Models;
using DataboxConnector.Sources.Spotify;
using DataboxConnector.Sources.Spotify.Configuration;
using DataboxConnector.Sources.Spotify.Internal;
using DataboxConnector.Sources.Spotify.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DataboxConnector.Sources.Spotify.Tests.Sources;

public class SpotifyRecentlyPlayedSourceTests
{
    private static SpotifyRecentlyPlayedSource NewSource(ISpotifyApiClient api) =>
        new(api,
            Options.Create(new SpotifyOptions
            {
                ClientId = "id",
                ClientSecret = "secret",
                DefaultLookbackDays = 7
            }),
            NullLogger<SpotifyRecentlyPlayedSource>.Instance);

    private static SpotifyPlayHistoryItem ValidPlay(string id) => new()
    {
        Track = new SpotifyTrack
        {
            Id = id,
            Name = "T",
            Artists = new() { new() { Name = "A" } },
            Album = new() { Name = "Alb" }
        },
        PlayedAt = DateTime.UtcNow
    };

    [Fact]
    public void SourceName_IsRecentlyPlayed()
    {
        NewSource(new FakeApi()).SourceName.Should().Be("spotify_recently_played");
    }

    [Fact]
    public void Schema_IsRecentlyPlayedSchema()
    {
        NewSource(new FakeApi()).Schema.Key.Should().Be("spotify_recently_played_v1");
    }

    [Fact]
    public async Task ExtractAsync_StreamsAllItems()
    {
        var fake = new FakeApi
        {
            RecentlyPlayed = new List<SpotifyPlayHistoryItem> { ValidPlay("a"), ValidPlay("b") }
        };

        var source = NewSource(fake);

        var records = new List<RawRecord>();
        await foreach (var r in source.ExtractAsync(new ExtractionContext()))
            records.Add(r);

        records.Should().HaveCount(2);
        records.Select(r => r.Fields["track_id"]).Should().Equal("a", "b");
    }

    [Fact]
    public async Task ExtractAsync_SkipsMalformedItems()
    {
        var fake = new FakeApi
        {
            RecentlyPlayed = new List<SpotifyPlayHistoryItem>
            {
                ValidPlay("ok"),
                new() { Track = null, PlayedAt = DateTime.UtcNow }, // malformed
                ValidPlay("ok2")
            }
        };

        var source = NewSource(fake);

        var records = new List<RawRecord>();
        await foreach (var r in source.ExtractAsync(new ExtractionContext()))
            records.Add(r);

        records.Should().HaveCount(2);
    }

    private sealed class FakeApi : ISpotifyApiClient
    {
        public List<SpotifyPlayHistoryItem> RecentlyPlayed { get; set; } = new();
        public List<SpotifyTrack> TopTracks { get; set; } = new();

        public Task<IReadOnlyList<SpotifyPlayHistoryItem>> GetRecentlyPlayedAsync(
            DateTimeOffset after, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyPlayHistoryItem>>(RecentlyPlayed);

        public Task<IReadOnlyList<SpotifyTrack>> GetTopTracksAsync(
            string timeRange, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyTrack>>(TopTracks);
    }
}