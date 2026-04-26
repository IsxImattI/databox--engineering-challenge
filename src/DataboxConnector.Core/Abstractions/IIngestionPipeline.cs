using DataboxConnector.Core.Models;

namespace DataboxConnector.Core.Abstractions;

/// <summary>
/// Orchestrates a single end-to-end ingestion run for one source/sink pair.
/// </summary>
/// <remarks>
/// The pipeline is responsible for:
/// <list type="bullet">
/// <item>Streaming records out of the source.</item>
/// <item>Validating each record against the schema.</item>
/// <item>Batching records for the sink (respecting any size limit the sink imposes).</item>
/// <item>Producing a structured <see cref="IngestionResult"/> capturing what happened.</item>
/// </list>
/// </remarks>
public interface IIngestionPipeline
{
    /// <summary>
    /// Runs an extraction-validation-load cycle.
    /// </summary>
    /// <param name="source">The source to extract from.</param>
    /// <param name="sink">The sink to write to.</param>
    /// <param name="context">Extraction context (time window, correlation id).</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>
    /// A non-null <see cref="IngestionResult"/>. The pipeline does not throw on
    /// expected failures; instead, the result captures success or the error message.
    /// Unexpected failures (programmer errors, OOM) propagate as exceptions.
    /// </returns>
    Task<IngestionResult> RunAsync(
        ISourceConnector source,
        ISinkConnector sink,
        ExtractionContext context,
        CancellationToken cancellationToken = default);
}