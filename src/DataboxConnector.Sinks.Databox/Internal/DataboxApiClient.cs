using System.Net.Http.Json;
using System.Text.Json;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;
using DataboxConnector.Sinks.Databox.Models;
using Microsoft.Extensions.Logging;

namespace DataboxConnector.Sinks.Databox.Internal;

/// <summary>
/// HTTP-backed implementation of <see cref="IDataboxApiClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Constructed by <see cref="HttpClient"/> typed-client DI. The base address
/// and <c>x-api-key</c> header are configured by
/// <see cref="DependencyInjection.DataboxSinkServiceCollectionExtensions"/>.
/// </para>
/// <para>
/// Resilience (retries, timeouts) is also configured at registration time
/// via <c>Microsoft.Extensions.Http.Resilience</c>; this class assumes a
/// transient failure has already been retried by the time it sees an error response.
/// </para>
/// </remarks>
internal sealed class DataboxApiClient : IDataboxApiClient
{
    private const string SinkName = "databox";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ILogger<DataboxApiClient> _logger;

    public DataboxApiClient(HttpClient http, ILogger<DataboxApiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);

        _http = http;
        _logger = logger;
    }

    public async Task<string> CreateDataSourceAsync(string title, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var payload = new { title };

        _logger.LogInformation("Creating Databox data source '{Title}'.", title);

        using var response = await _http
            .PostAsJsonAsync("/v1/data-sources", payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var body = await ReadOrThrowAsync<DataboxDataSourceResponse>(response, "create data source", cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(body.Id))
            throw new SinkIngestionException(SinkName, "Databox returned no data source id.");

        _logger.LogInformation("Created Databox data source: id={Id} title={Title}", body.Id, body.Title);
        return body.Id;
    }

    public async Task<string> CreateDatasetAsync(
        string dataSourceId,
        DatasetSchema schema,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);
        ArgumentNullException.ThrowIfNull(schema);

        // Databox dataset id is numeric in the API spec ("Example: 4754489").
        // Locally we always work with the string form.
        var payload = new
        {
            title = schema.Title,
            dataSourceId = long.Parse(dataSourceId),
            primaryKeys = schema.PrimaryKeys.Count > 0 ? schema.PrimaryKeys : null
        };

        _logger.LogInformation(
            "Creating Databox dataset '{Title}' (key={Key}) under data source {DataSourceId}.",
            schema.Title, schema.Key, dataSourceId);

        using var response = await _http
            .PostAsJsonAsync("/v1/datasets", payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var body = await ReadOrThrowAsync<DataboxDatasetResponse>(response, "create dataset", cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(body.Id))
            throw new SinkIngestionException(SinkName, "Databox returned no dataset id.");

        _logger.LogInformation("Created Databox dataset: id={Id} title={Title}", body.Id, body.Title);
        return body.Id;
    }

    public async Task<string> IngestRecordsAsync(
        string datasetId,
        IReadOnlyList<RawRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
            throw new ArgumentException("Cannot ingest an empty batch.", nameof(records));
        if (records.Count > 100)
            throw new ArgumentException("Databox limits ingestions to 100 records per request.", nameof(records));

        var payload = new
        {
            records = records.Select(r => r.Fields).ToList()
        };

        var url = $"/v1/datasets/{Uri.EscapeDataString(datasetId)}/data";

        _logger.LogDebug("Ingesting {Count} records into Databox dataset {DatasetId}.", records.Count, datasetId);

        using var response = await _http
            .PostAsJsonAsync(url, payload, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var body = await ReadOrThrowAsync<DataboxIngestionResponse>(response, "ingest records", cancellationToken)
            .ConfigureAwait(false);

        return body.IngestionId ?? "<no-id>";
    }

    /// <summary>
    /// Reads the response body. Throws <see cref="SinkIngestionException"/> on non-success
    /// or unparseable bodies, mapping the Databox error envelope to a meaningful message.
    /// </summary>
    private async Task<T> ReadOrThrowAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
        where T : class
    {
        if (response.IsSuccessStatusCode)
        {
            try
            {
                var body = await response.Content
                    .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (body is null)
                    throw new SinkIngestionException(SinkName,
                        $"Databox returned an empty body for {operation}.");

                return body;
            }
            catch (JsonException ex)
            {
                throw new SinkIngestionException(SinkName,
                    $"Failed to parse Databox response for {operation}: {ex.Message}", ex);
            }
        }

        // Non-success: try to parse the structured error envelope.
        var raw = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        string detail;
        try
        {
            var error = JsonSerializer.Deserialize<DataboxErrorResponse>(raw, JsonOptions);
            detail = error?.Errors is { Count: > 0 } errors
                ? string.Join("; ", errors.Select(FormatError))
                : raw;
        }
        catch (JsonException)
        {
            detail = raw;
        }

        throw new SinkIngestionException(SinkName,
            $"Databox API call '{operation}' failed with status {(int)response.StatusCode}: {detail}");
    }

    private static string FormatError(DataboxErrorItem error)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(error.Code))    parts.Add($"code={error.Code}");
        if (!string.IsNullOrEmpty(error.Field))   parts.Add($"field={error.Field}");
        if (!string.IsNullOrEmpty(error.Type))    parts.Add($"type={error.Type}");
        if (!string.IsNullOrEmpty(error.Message)) parts.Add(error.Message);
        return string.Join(", ", parts);
    }
}