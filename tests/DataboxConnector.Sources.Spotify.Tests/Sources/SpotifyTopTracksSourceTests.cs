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

public class SpotifyTopTracksSourceTests
{
    private static SpotifyTopTracksSource NewSource(
        ISpotifyApiClient api,
        SpotifyOptions? overrides = null) =>
        new(api,
            Options.Create(overrides ?? new SpotifyOptions
            {
                ClientId = "id",
                ClientSecret = "secret",
                TopTrackTimeRanges = new() { "short_term", "medium_term" },
                TopTracksLimit = 10
            }),
            NullLogger<SpotifyTopTracksSource>.Instance);

    private static SpotifyTrack ValidTrack(string id) => new()
    {
        Id = id,
        Name = "T",
        Artists = new() { new() { Name = "A" } },
        Album = new() { Name = "Alb" }
    };

    [Fact]
    public void SourceName_IsTopTracks()
    {
        NewSource(new FakeApi()).SourceName.Should().Be("spotify_top_tracks");
    }

    [Fact]
    public void Schema_IsTopTracksSchema()
    {
        NewSource(new FakeApi()).Schema.Key.Should().Be("spotify_top_tracks_v1");
    }

    [Fact]
    public async Task ExtractAsync_TwoTimeRanges_TwoTracksEach_YieldsFourRecords()
    {
        var fake = new FakeApi
        {
            TopTracksByRange = new Dictionary<string, List<SpotifyTrack>>
            {
                ["short_term"]  = new() { ValidTrack("s1"), ValidTrack("s2") },
                ["medium_term"] = new() { ValidTrack("m1"), ValidTrack("m2") }
            }
        };

        var records = new List<RawRecord>();
        await foreach (var r in NewSource(fake).ExtractAsync(new ExtractionContext()))
            records.Add(r);

        records.Should().HaveCount(4);
    }

    [Fact]
    public async Task ExtractAsync_SnapshotShareCapturedAtAndRangeAndRanks()
    {
        var fake = new FakeApi
        {
            TopTracksByRange = new Dictionary<string, List<SpotifyTrack>>
            {
                ["short_term"] = new() { ValidTrack("a"), ValidTrack("b"), ValidTrack("c") }
            }
        };

        var options = new SpotifyOptions
        {
            ClientId = "id",
            ClientSecret = "secret",
            TopTrackTimeRanges = new() { "short_term" },
            TopTracksLimit = 10
        };

        var ctx = new ExtractionContext { To = new DateTimeOffset(2026, 4, 1, 10, 0, 0, TimeSpan.Zero) };

        var records = new List<RawRecord>();
        await foreach (var r in NewSource(fake, options).ExtractAsync(ctx))
            records.Add(r);

        records.Should().HaveCount(3);

        // All have same captured_at and time_range
        records.Select(r => r.Fields["captured_at"]).Distinct().Should().HaveCount(1);
        records.Select(r => r.Fields["time_range"]).Distinct().Should().ContainSingle()
            .Which.Should().Be("short_term");

        // Ranks are 1-based and sequential
        records.Select(r => r.Fields["rank"]).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ExtractAsync_EmptyTimeRangeResults_YieldsNothingForThatRange()
    {
        var fake = new FakeApi
        {
            TopTracksByRange = new Dictionary<string, List<SpotifyTrack>>
            {
                ["short_term"]  = new() { ValidTrack("a") },
                ["medium_term"] = new()
            }
        };

        var records = new List<RawRecord>();
        await foreach (var r in NewSource(fake).ExtractAsync(new ExtractionContext()))
            records.Add(r);

        records.Should().HaveCount(1);
    }

    private sealed class FakeApi : ISpotifyApiClient
    {
        public Dictionary<string, List<SpotifyTrack>> TopTracksByRange { get; set; } = new();

        public Task<IReadOnlyList<SpotifyPlayHistoryItem>> GetRecentlyPlayedAsync(
            DateTimeOffset after, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SpotifyPlayHistoryItem>>(Array.Empty<SpotifyPlayHistoryItem>());

        public Task<IReadOnlyList<SpotifyTrack>> GetTopTracksAsync(
            string timeRange, int limit, CancellationToken cancellationToken = default)
        {
            var tracks = TopTracksByRange.TryGetValue(timeRange, out var list)
                ? list
                : new List<SpotifyTrack>();
            return Task.FromResult<IReadOnlyList<SpotifyTrack>>(tracks);
        }
    }
}