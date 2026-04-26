namespace DataboxConnector.Core.Models;

/// <summary>
/// Outcome of a single ingestion run for one source.
/// </summary>
public sealed record IngestionResult
{
    public required string SourceName { get; init; }
    public required string DatasetKey { get; init; }
    public required string CorrelationId { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required int RecordsExtracted { get; init; }
    public required int RecordsSent { get; init; }
    public required int BatchesSent { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public TimeSpan Duration => CompletedAt - StartedAt;
}