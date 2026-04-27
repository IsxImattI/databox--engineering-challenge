using System.Text.Json;
using DataboxConnector.Sinks.Databox.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataboxConnector.Sinks.Databox.Internal;

/// <summary>
/// JSON-file-backed implementation of <see cref="IDataboxIdentifierStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// All access is serialized through a <see cref="SemaphoreSlim"/> to make the store
/// safe to call from multiple jobs concurrently.
/// </para>
/// <para>
/// The file is read on the first call, kept in memory thereafter, and written
/// out on every mutation. The file is small (a handful of UUIDs), so this is fine.
/// </para>
/// </remarks>
internal sealed class FileBasedIdentifierStore : IDataboxIdentifierStore
{
    private readonly string _filePath;
    private readonly ILogger<FileBasedIdentifierStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IdentifierData? _cached;

    public FileBasedIdentifierStore(
        IOptions<DataboxOptions> options,
        ILogger<FileBasedIdentifierStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _filePath = Path.GetFullPath(options.Value.IdentifierStorePath);
        _logger = logger;
    }

    public async Task<string?> GetDataSourceIdAsync(CancellationToken cancellationToken = default)
    {
        var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return data.DataSourceId;
    }

    public async Task SetDataSourceIdAsync(string dataSourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            data.DataSourceId = dataSourceId;
            await SaveInternalAsync(data, cancellationToken).ConfigureAwait(false);
            _cached = data;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> GetDatasetIdAsync(string datasetKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetKey);
        var data = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return data.DatasetIds.TryGetValue(datasetKey, out var id) ? id : null;
    }

    public async Task SetDatasetIdAsync(string datasetKey, string datasetId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var data = await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
            data.DatasetIds[datasetKey] = datasetId;
            await SaveInternalAsync(data, cancellationToken).ConfigureAwait(false);
            _cached = data;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IdentifierData> LoadAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null) return _cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _cached ??= await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IdentifierData> LoadInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogDebug("Identifier store not found at {Path}; starting empty.", _filePath);
            return new IdentifierData();
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var data = await JsonSerializer
                .DeserializeAsync<IdentifierData>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return data ?? new IdentifierData();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Identifier store at {Path} is corrupt; ignoring and starting empty.", _filePath);
            return new IdentifierData();
        }
    }

    private async Task SaveInternalAsync(IdentifierData data, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(_filePath);
        await JsonSerializer
            .SerializeAsync(stream, data, new JsonSerializerOptions { WriteIndented = true }, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Identifier store saved to {Path}.", _filePath);
    }

    private sealed class IdentifierData
    {
        public string? DataSourceId { get; set; }
        public Dictionary<string, string> DatasetIds { get; set; } = new();
    }
}