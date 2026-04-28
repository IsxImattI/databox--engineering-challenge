using DataboxConnector.Sources.Spotify.Mapping;
using DataboxConnector.Sources.Spotify.Models;
using FluentAssertions;
using Xunit;

namespace DataboxConnector.Sources.Spotify.Tests.Mapping;

public class SpotifyMappersTests
{
    private static SpotifyTrack ValidTrack(string id = "t1") => new()
    {
        Id = id,
        Name = "Test Track",
        Artists = new List<SpotifyArtist> { new() { Name = "Test Artist" } },
        Album = new SpotifyAlbum { Name = "Test Album" },
        DurationMs = 200_000,
        Popularity = 75,
        Explicit = false
    };

    private static SpotifyPlayHistoryItem ValidPlay() => new()
    {
        Track = ValidTrack(),
        PlayedAt = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc)
    };

    // ---------- MapPlayHistoryItem ----------

    [Fact]
    public void MapPlayHistoryItem_ValidItem_MapsAllFields()
    {
        var record = SpotifyMappers.MapPlayHistoryItem(ValidPlay());

        record.Should().NotBeNull();
        record!.Fields["track_id"].Should().Be("t1");
        record.Fields["track_name"].Should().Be("Test Track");
        record.Fields["artist_name"].Should().Be("Test Artist");
        record.Fields["album_name"].Should().Be("Test Album");
        record.Fields["duration_ms"].Should().Be(200_000);
        record.Fields["popularity"].Should().Be(75);
        record.Fields["explicit"].Should().Be(false);
    }

    [Fact]
    public void MapPlayHistoryItem_NoTrack_ReturnsNull()
    {
        var play = ValidPlay();
        play.Track = null;

        SpotifyMappers.MapPlayHistoryItem(play).Should().BeNull();
    }

    [Fact]
    public void MapPlayHistoryItem_TrackIdMissing_ReturnsNull()
    {
        var play = ValidPlay();
        play.Track!.Id = null;

        SpotifyMappers.MapPlayHistoryItem(play).Should().BeNull();
    }

    [Fact]
    public void MapPlayHistoryItem_TrackNameMissing_ReturnsNull()
    {
        var play = ValidPlay();
        play.Track!.Name = null;

        SpotifyMappers.MapPlayHistoryItem(play).Should().BeNull();
    }

    [Fact]
    public void MapPlayHistoryItem_NoArtists_ReturnsNull()
    {
        var play = ValidPlay();
        play.Track!.Artists = null;

        SpotifyMappers.MapPlayHistoryItem(play).Should().BeNull();
    }

    [Fact]
    public void MapPlayHistoryItem_NoAlbumName_ReturnsNull()
    {
        var play = ValidPlay();
        play.Track!.Album = null;

        SpotifyMappers.MapPlayHistoryItem(play).Should().BeNull();
    }

    [Fact]
    public void MapPlayHistoryItem_MultipleArtists_TakesFirst()
    {
        var play = ValidPlay();
        play.Track!.Artists = new List<SpotifyArtist>
        {
            new() { Name = "Primary" },
            new() { Name = "Featured" }
        };

        var record = SpotifyMappers.MapPlayHistoryItem(play);

        record!.Fields["artist_name"].Should().Be("Primary");
    }

    [Fact]
    public void MapPlayHistoryItem_PlayedAtNormalizedToUtc()
    {
        var play = ValidPlay();
        play.PlayedAt = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Local);

        var record = SpotifyMappers.MapPlayHistoryItem(play);

        record.Should().NotBeNull();
        ((DateTime)record!.Fields["played_at"]!).Kind.Should().Be(DateTimeKind.Utc);
    }

    // ---------- MapTopTrack ----------

    [Fact]
    public void MapTopTrack_ValidTrack_MapsAllFields()
    {
        var capturedAt = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);

        var record = SpotifyMappers.MapTopTrack(ValidTrack("t99"), "short_term", rank: 3, capturedAt);

        record.Should().NotBeNull();
        record!.Fields["captured_at"].Should().Be(capturedAt.UtcDateTime);
        record.Fields["time_range"].Should().Be("short_term");
        record.Fields["rank"].Should().Be(3);
        record.Fields["track_id"].Should().Be("t99");
        record.Fields["track_name"].Should().Be("Test Track");
        record.Fields["artist_name"].Should().Be("Test Artist");
        record.Fields["popularity"].Should().Be(75);
    }

    [Fact]
    public void MapTopTrack_TrackIdMissing_ReturnsNull()
    {
        var track = ValidTrack();
        track.Id = null;

        SpotifyMappers.MapTopTrack(track, "short_term", 1, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void MapTopTrack_NoArtists_ReturnsNull()
    {
        var track = ValidTrack();
        track.Artists = null;

        SpotifyMappers.MapTopTrack(track, "short_term", 1, DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Fact]
    public void MapTopTrack_EmptyTimeRange_Throws()
    {
        var track = ValidTrack();

        var act = () => SpotifyMappers.MapTopTrack(track, "", 1, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }
}