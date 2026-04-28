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
/// Source connector that streams the user's recently played tracks.
/// </summary>
/// <remarks>
/// Spotify's <c>/me/player/recently-played</c> endpoint exposes only the last
/// 50 plays regardless of the time window, so the source is naturally bounded.
/// Combined with the dataset's primary key (<c>played_at</c> + <c>track_id</c>),
/// duplicates from overlapping runs are deduplicated by Databox.
/// </remarks>
public sealed class SpotifyRecentlyPlayedSource : ISourceConnector
{
    public string SourceName => "spotify_recently_played";
    public DatasetSchema Schema => SpotifyRecentlyPlayedSchema.Instance;

    private readonly ISpotifyApiClient _api;
    private readonly SpotifyOptions _options;
    private readonly ILogger<SpotifyRecentlyPlayedSource> _logger;

    internal SpotifyRecentlyPlayedSource(
        ISpotifyApiClient api,
        IOptions<SpotifyOptions> options,
        ILogger<SpotifyRecentlyPlayedSource> logger)
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

        var since = context.From ?? DateTimeOffset.UtcNow.AddDays(-_options.DefaultLookbackDays);
        var emitted = 0;

        _logger.LogInformation("Extracting Spotify recently played since {Since}.", since);

        var items = await _api.GetRecentlyPlayedAsync(since, cancellationToken).ConfigureAwait(false);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = SpotifyMappers.MapPlayHistoryItem(item);
            if (record is null)
            {
                _logger.LogDebug("Skipping malformed play history item.");
                continue;
            }

            yield return record;
            emitted++;
        }

        _logger.LogInformation(
            "Spotify recently played extraction completed: {Total} records.", emitted);
    }
}