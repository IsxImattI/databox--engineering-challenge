using DataboxConnector.Core.Abstractions;
using DataboxConnector.Core.Exceptions;
using DataboxConnector.Core.Models;
using DataboxConnector.Core.Schema;
using Microsoft.Extensions.Logging;

namespace DataboxConnector.Core.Pipeline;

/// <summary>
/// Default <see cref="IIngestionPipeline"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// Streams records from the source, validates them against the schema, batches them,
/// and forwards each batch to the sink. Stops at the first sink failure (sinks are
/// expected to handle their own retry/backoff internally — the pipeline does not
/// duplicate that responsibility).
/// </para>
/// <para>
/// All outcomes (success, partial failure, total failure) are captured in the returned
/// <see cref="IngestionResult"/> rather than thrown, so callers can log uniformly.
/// </para>
/// </remarks>
public sealed class IngestionPipeline : IIngestionPipeline
{
    /// <summary>
    /// Records per batch. Aligned with Databox Ingestion API limit of 100 records per request.
    /// </summary>
    public const int DefaultBatchSize = 100;

    private readonly ILogger<IngestionPipeline> _logger;
    private readonly int _batchSize;

    public IngestionPipeline(ILogger<IngestionPipeline> logger, int batchSize = DefaultBatchSize)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Batch size must be between 1 and 1000 (Databox payload cap).");

        _logger = logger;
        _batchSize = batchSize;
    }

    public async Task<IngestionResult> RunAsync(
        ISourceConnector source,
        ISinkConnector sink,
        ExtractionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(context);

        var startedAt = DateTimeOffset.UtcNow;
        var schema = source.Schema;
        var extracted = 0;
        var sent = 0;
        var batches = 0;
        string? errorMessage = null;
        var success = false;

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = context.CorrelationId,
            ["Source"]        = source.SourceName,
            ["Sink"]          = sink.SinkName,
            ["DatasetKey"]    = schema.Key
        });

        _logger.LogInformation(
            "Starting ingestion: source={Source} sink={Sink} dataset={Dataset} window=[{From} → {To}]",
            source.SourceName, sink.SinkName, schema.Key, context.From, context.To);

        try
        {
            await sink.EnsureReadyAsync(schema, cancellationToken).ConfigureAwait(false);

            var buffer = new List<RawRecord>(_batchSize);

            await foreach (var record in source.ExtractAsync(context, cancellationToken)
                                                .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                SchemaValidator.Validate(schema, record);
                extracted++;
                buffer.Add(record);

                if (buffer.Count >= _batchSize)
                {
                    sent += await FlushBatchAsync(sink, schema, buffer, cancellationToken)
                        .ConfigureAwait(false);
                    batches++;
                    buffer.Clear();
                }
            }

            if (buffer.Count > 0)
            {
                sent += await FlushBatchAsync(sink, schema, buffer, cancellationToken)
                    .ConfigureAwait(false);
                batches++;
            }

            success = true;
            _logger.LogInformation(
                "Ingestion completed: extracted={Extracted} sent={Sent} batches={Batches}",
                extracted, sent, batches);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            errorMessage = "Cancelled.";
            _logger.LogWarning("Ingestion cancelled after extracting {Extracted} records.", extracted);
        }
        catch (SchemaValidationException ex)
        {
            errorMessage = $"Schema validation failed: {ex.Message}";
            _logger.LogError(ex, "Ingestion aborted due to schema violation.");
        }
        catch (SourceExtractionException ex)
        {
            errorMessage = ex.Message;
            _logger.LogError(ex, "Ingestion aborted due to source failure.");
        }
        catch (SinkIngestionException ex)
        {
            errorMessage = ex.Message;
            _logger.LogError(ex, "Ingestion aborted due to sink failure.");
        }
        catch (Exception ex)
        {
            errorMessage = $"Unexpected failure: {ex.Message}";
            _logger.LogError(ex, "Ingestion aborted due to unexpected error.");
        }

        return new IngestionResult
        {
            SourceName        = source.SourceName,
            DatasetKey        = schema.Key,
            CorrelationId     = context.CorrelationId,
            StartedAt         = startedAt,
            CompletedAt       = DateTimeOffset.UtcNow,
            RecordsExtracted  = extracted,
            RecordsSent       = sent,
            BatchesSent       = batches,
            Success           = success,
            ErrorMessage      = errorMessage
        };
    }

    private async Task<int> FlushBatchAsync(
        ISinkConnector sink,
        DatasetSchema schema,
        IReadOnlyList<RawRecord> batch,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Flushing batch of {Count} records to sink {Sink}.", batch.Count, sink.SinkName);
        return await sink.SendAsync(schema, batch, cancellationToken).ConfigureAwait(false);
    }
}