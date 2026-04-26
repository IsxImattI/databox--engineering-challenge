using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;

namespace DataboxConnector.Core.Abstractions;

/// <summary>
/// Delivers records to a downstream system (e.g. Databox, a file, or a stdout dry-run).
/// </summary>
/// <remarks>
/// Sinks are responsible for batching and any sink-specific protocol concerns
/// (auth, rate limiting, retries). They consume <see cref="RawRecord"/> instances
/// that have already been validated against their schema by the pipeline.
/// </remarks>
public interface ISinkConnector
{
    /// <summary>
    /// Stable identifier of the sink (e.g. <c>databox</c>).
    /// </summary>
    string SinkName { get; }

    /// <summary>
    /// Sends a batch of records belonging to the given schema.
    /// </summary>
    /// <param name="schema">The schema the records conform to.</param>
    /// <param name="records">A batch of records to send. Implementations may impose batch size limits.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>The number of records the sink accepted.</returns>
    /// <exception cref="Exceptions.SinkIngestionException">
    /// Thrown when delivery fails permanently (after retries are exhausted).
    /// </exception>
    Task<int> SendAsync(
        DatasetSchema schema,
        IReadOnlyList<RawRecord> records,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures any setup required before records can be sent has been performed.
    /// For example, a Databox sink may use this to ensure the dataset exists.
    /// Idempotent: calling multiple times must not duplicate state.
    /// </summary>
    Task EnsureReadyAsync(
        DatasetSchema schema,
        CancellationToken cancellationToken = default);
}