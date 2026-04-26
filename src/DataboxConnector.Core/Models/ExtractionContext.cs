namespace DataboxConnector.Core.Models;

/// <summary>
/// Context passed to a source connector when initiating an extraction.
/// </summary>
/// <remarks>
/// Carries the time window the source should extract data for. The window
/// supports incremental extraction: a scheduler can pass <c>From = lastSuccessfulRun</c>
/// to fetch only new data since the previous run.
/// </remarks>
public sealed record ExtractionContext
{
    /// <summary>
    /// Inclusive lower bound. If <c>null</c>, the source is free to choose
    /// a sensible default (e.g. last 30 days, or its own watermark).
    /// </summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// Exclusive upper bound. Defaults to <see cref="DateTimeOffset.UtcNow"/>
    /// at the time of construction.
    /// </summary>
    public DateTimeOffset To { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Correlation identifier for tracing this extraction across logs.
    /// </summary>
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
}