using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;

namespace DataboxConnector.Sinks.Databox.Internal;

/// <summary>
/// Low-level wrapper around the three Databox Ingestion API endpoints we use.
/// </summary>
/// <remarks>
/// Hides HTTP plumbing from <see cref="DataboxSink"/>, which only deals with
/// domain concepts (data source ID, dataset ID, records).
/// </remarks>
internal interface IDataboxApiClient
{
    /// <summary>
    /// Creates a new data source with the given title.
    /// </summary>
    /// <returns>The new data source ID.</returns>
    /// <exception cref="Core.Exceptions.SinkIngestionException">
    /// Thrown when the API responds with an error.
    /// </exception>
    Task<string> CreateDataSourceAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new dataset within the given data source.
    /// </summary>
    /// <returns>The new dataset ID.</returns>
    Task<string> CreateDatasetAsync(
        string dataSourceId,
        DatasetSchema schema,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests a batch of records into the dataset.
    /// Databox limits each request to 100 records, so the caller must batch upstream.
    /// </summary>
    /// <returns>The Databox-issued ingestion ID for tracing.</returns>
    Task<string> IngestRecordsAsync(
        string datasetId,
        IReadOnlyList<RawRecord> records,
        CancellationToken cancellationToken = default);
}