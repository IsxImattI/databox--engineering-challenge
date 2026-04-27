namespace DataboxConnector.Sinks.Databox.Internal;

/// <summary>
/// Persists the Databox-issued identifiers (data source ID, per-dataset IDs)
/// so the sink does not provision them on every startup.
/// </summary>
/// <remarks>
/// The default file-based implementation is sufficient for local development.
/// In production this could be replaced with a database-backed store or
/// a managed secret store, transparently to the sink.
/// </remarks>
public interface IDataboxIdentifierStore
{
    Task<string?> GetDataSourceIdAsync(CancellationToken cancellationToken = default);
    Task SetDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default);

    Task<string?> GetDatasetIdAsync(string datasetKey, CancellationToken cancellationToken = default);
    Task SetDatasetIdAsync(string datasetKey, string datasetId, CancellationToken cancellationToken = default);
}