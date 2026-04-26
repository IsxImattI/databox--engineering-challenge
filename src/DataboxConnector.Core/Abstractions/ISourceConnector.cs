using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;

namespace DataboxConnector.Core.Abstractions;

/// <summary>
/// Extracts raw data from an external system.
/// </summary>
/// <remarks>
/// <para>
/// Implementations should be stateless with respect to a single extraction:
/// all state needed to drive paging or watermarks is carried in the
/// <see cref="ExtractionContext"/> or held privately.
/// </para>
/// <para>
/// Records are streamed via <see cref="IAsyncEnumerable{T}"/> so callers
/// can begin batching and forwarding without holding the entire result set
/// in memory.
/// </para>
/// </remarks>
public interface ISourceConnector
{
    /// <summary>
    /// Stable, lowercase identifier (e.g. <c>github_commits</c>, <c>spotify_recently_played</c>).
    /// Must match the schema's <see cref="DatasetSchema.Key"/>.
    /// </summary>
    string SourceName { get; }

    /// <summary>
    /// The schema of records this source produces.
    /// </summary>
    DatasetSchema Schema { get; }

    /// <summary>
    /// Streams records extracted from the source for the given context window.
    /// </summary>
    /// <exception cref="Exceptions.SourceExtractionException">
    /// Thrown when extraction fails. Wraps the underlying exception as inner.
    /// </exception>
    IAsyncEnumerable<RawRecord> ExtractAsync(
        ExtractionContext context,
        CancellationToken cancellationToken = default);
}