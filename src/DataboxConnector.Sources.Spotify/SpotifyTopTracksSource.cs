using System.Runtime.CompilerServices;
using DataboxConnector.Core.Abstractions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;
using DataboxConnector.Sources.Spotify.Configuration;
using DataboxConnector.Sources.Spotify.Internal;
using DataboxConnector.Sources.Spotify.Mapping;
using DataboxConnector.Sources.Spotify.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sources.Spotify;

/// <summary>
/// Source connector that captures snapshots of the user's top tracks across
/// the configured time ranges (<c>short_term</c>, <c>medium_term</c>, <c>long_term</c>).
/// </summary>
/// <remarks>
/// Each run produces N x M records, where N is the number of configured time
/// ranges and M is <see cref="SpotifyOptions.TopTracksLimit"/>. All records
/// from a single run share the same <c>captured_at</c> timestamp, which acts
/// as the snapshot identifier in the dataset.
/// </remarks>
public sealed class SpotifyTopTracksSource : ISourceConnector
{
    public string SourceName => "spotify_top_tracks";
    public DatasetSchema Schema => SpotifyTopTracksSchema.Instance;

    private readonly ISpotifyApiClient _api;
    private readonly SpotifyOptions _options;
    private readonly ILogger<SpotifyTopTracksSource> _logger;

    internal SpotifyTopTracksSource(
        ISpotifyApiClient api,
        IOptions<SpotifyOptions> options,
        ILogger<SpotifyTopTracksSource> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<RawRecord> ExtractAsync(
        ExtractionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var capturedAt = context.To;
        var emitted = 0;

        _logger.LogInformation(
            "Capturing Spotify top tracks snapshot at {CapturedAt} across {Count} time ranges.",
            capturedAt, _options.TopTrackTimeRanges.Count);

        foreach (var timeRange in _options.TopTrackTimeRanges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tracks = await _api
                .GetTopTracksAsync(timeRange, _options.TopTracksLimit, cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < tracks.Count; i++)
            {
                var record = SpotifyMappers.MapTopTrack(tracks[i], timeRange, rank: i + 1, capturedAt);
                if (record is null)
                {
                    _logger.LogDebug(
                        "Skipping malformed top track at rank {Rank} for {TimeRange}.",
                        i + 1, timeRange);
                    continue;
                }

                yield return record;
                emitted++;
            }

            _logger.LogInformation(
                "Captured {Count} top tracks for {TimeRange}.", tracks.Count, timeRange);
        }

        _logger.LogInformation(
            "Spotify top tracks snapshot completed: {Total} records.", emitted);
    }
}