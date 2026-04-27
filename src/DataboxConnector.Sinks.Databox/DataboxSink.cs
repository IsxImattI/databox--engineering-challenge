using DataboxConnector.Core.Abstractions;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;
using DataboxConnector.Sinks.Databox.Configuration;
using DataboxConnector.Sinks.Databox.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sinks.Databox;

/// <summary>
/// <see cref="ISinkConnector"/> implementation that delivers records to the
/// Databox Ingestion API.
/// </summary>
/// <remarks>
/// <para>
/// Provisions data source and datasets lazily on first use. Identifiers are
/// cached locally via <see cref="IDataboxIdentifierStore"/> so subsequent runs
/// do not hit the create endpoints again.
/// </para>
/// <para>
/// HTTP-level concerns (retries, timeouts, auth) are handled by the
/// <see cref="HttpClient"/> pipeline configured at registration time.
/// </para>
/// </remarks>
public sealed class DataboxSink : ISinkConnector
{
    public string SinkName => "databox";

    private readonly IDataboxApiClient _api;
    private readonly IDataboxIdentifierStore _store;
    private readonly DataboxOptions _options;
    private readonly ILogger<DataboxSink> _logger;

    private readonly SemaphoreSlim _provisioningGate = new(1, 1);

    internal DataboxSink(
        IDataboxApiClient api,
        IDataboxIdentifierStore store,
        IOptions<DataboxOptions> options,
        ILogger<DataboxSink> logger)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _api = api;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureReadyAsync(DatasetSchema schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);

        // Fast path: both ids already known.
        var dataSourceId = await _store.GetDataSourceIdAsync(cancellationToken).ConfigureAwait(false);
        var datasetId    = await _store.GetDatasetIdAsync(schema.Key, cancellationToken).ConfigureAwait(false);

        if (dataSourceId is not null && datasetId is not null)
            return;

        // Slow path: serialize provisioning so concurrent jobs don't double-provision.
        await _provisioningGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            dataSourceId = await _store.GetDataSourceIdAsync(cancellationToken).ConfigureAwait(false);
            if (dataSourceId is null)
            {
                _logger.LogInformation("No cached data source id; creating one in Databox.");
                dataSourceId = await _api
                    .CreateDataSourceAsync(_options.DataSourceTitle, cancellationToken)
                    .ConfigureAwait(false);
                await _store.SetDataSourceIdAsync(dataSourceId, cancellationToken).ConfigureAwait(false);
            }

            datasetId = await _store.GetDatasetIdAsync(schema.Key, cancellationToken).ConfigureAwait(false);
            if (datasetId is null)
            {
                _logger.LogInformation(
                    "No cached dataset id for {DatasetKey}; creating it in Databox.", schema.Key);
                datasetId = await _api
                    .CreateDatasetAsync(dataSourceId, schema, cancellationToken)
                    .ConfigureAwait(false);
                await _store.SetDatasetIdAsync(schema.Key, datasetId, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _provisioningGate.Release();
        }
    }

    public async Task<int> SendAsync(
        DatasetSchema schema,
        IReadOnlyList<RawRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0) return 0;
        if (records.Count > 100)
            throw new ArgumentException(
                "DataboxSink expects pre-batched input of <=100 records (the Ingestion API cap). " +
                "Use IngestionPipeline which already batches.",
                nameof(records));

        var datasetId = await _store
            .GetDatasetIdAsync(schema.Key, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SinkIngestionException(
                SinkName,
                $"Dataset {schema.Key} is not provisioned; call EnsureReadyAsync first.");

        var ingestionId = await _api
            .IngestRecordsAsync(datasetId, records, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Ingested batch into Databox: dataset={DatasetKey} count={Count} ingestionId={IngestionId}",
            schema.Key, records.Count, ingestionId);

        return records.Count;
    }
}