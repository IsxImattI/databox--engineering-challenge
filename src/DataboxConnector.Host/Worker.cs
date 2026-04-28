using DataboxConnector.Core.Abstractions;
using DataboxConnector.Core.Models;

namespace DataboxConnector.Host;

/// <summary>
/// Background service that runs all configured sources once at startup.
/// </summary>
/// <remarks>
/// <para>
/// This is the simplest possible host: every source is extracted once,
/// then the worker idles until shutdown. Scheduled execution via Quartz
/// is added in a follow-up step.
/// </para>
/// <para>
/// The worker iterates all <see cref="ISourceConnector"/> registrations
/// and pairs each with the single configured <see cref="ISinkConnector"/>
/// (Databox). One pipeline run per source.
/// </para>
/// </remarks>
public sealed class Worker : BackgroundService
{
    private readonly IIngestionPipeline _pipeline;
    private readonly IEnumerable<ISourceConnector> _sources;
    private readonly ISinkConnector _sink;
    private readonly ILogger<Worker> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public Worker(
        IIngestionPipeline pipeline,
        IEnumerable<ISourceConnector> sources,
        ISinkConnector sink,
        ILogger<Worker> logger,
        IHostApplicationLifetime lifetime)
    {
        _pipeline = pipeline;
        _sources = sources;
        _sink = sink;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sourceList = _sources.ToList();

        _logger.LogInformation(
            "Worker starting with {Count} source(s) targeting sink={Sink}.",
            sourceList.Count, _sink.SinkName);

        foreach (var source in sourceList)
        {
            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogInformation("=== Running source: {Source} ===", source.SourceName);

            var result = await _pipeline.RunAsync(
                source,
                _sink,
                new ExtractionContext(),
                stoppingToken);

            if (result.Success)
            {
                _logger.LogInformation(
                    "  ✓ {Source}: extracted={Extracted} sent={Sent} batches={Batches} duration={Duration}",
                    source.SourceName, result.RecordsExtracted, result.RecordsSent,
                    result.BatchesSent, result.Duration);
            }
            else
            {
                _logger.LogError(
                    "  ✗ {Source} FAILED: {Error}",
                    source.SourceName, result.ErrorMessage);
            }
        }

        _logger.LogInformation("All sources processed. Stopping host.");
        _lifetime.StopApplication();
    }
}