# ADR-0001: Pipeline-based architecture over controller endpoints

**Status:** Accepted
**Date:** 2026-04-26

## Context

The challenge brief leaves the entry-point shape open. Two reasonable interpretations exist:

1. **Controller-based.** Expose HTTP endpoints (`POST /sync/github`, `POST /sync/spotify`) on a Web API project. An external scheduler (cron, Azure Function timer, Kubernetes CronJob) hits them on a schedule, the controller resolves the right source from DI, runs an extraction, and returns a result.

2. **Pipeline-based.** Define `IIngestionPipeline.RunAsync(source, sink, context)` as the central abstraction. A `BackgroundService` (or, later, a Quartz job) picks each registered `ISourceConnector` and runs the pipeline. There is no HTTP surface for the connector itself.

Both can satisfy the brief. The choice has consequences for how easy it is to add sources, where retry/scheduling logic lives, and how the codebase is layered.

## Decision

The connector is **pipeline-based**. The HTTP surface is omitted entirely; orchestration happens inside the host process via `IIngestionPipeline` invoked by a worker service.

The single abstraction is:

```csharp
public interface IIngestionPipeline
{
    Task<IngestionResult> RunAsync(
        ISourceConnector source,
        ISinkConnector sink,
        ExtractionContext context,
        CancellationToken cancellationToken);
}
```

The pipeline is the only thing that knows about batching, schema validation, and result accounting. Sources know nothing of sinks; sinks know nothing of sources; both know nothing of the pipeline.

## Consequences

### Positive

- **Adding a source is mechanical.** One class implementing `ISourceConnector`, one DI extension, no other code changes. The pipeline does not change. The worker does not change (it iterates `IEnumerable<ISourceConnector>`).
- **Schema validation is in one place.** The pipeline calls `SchemaValidator` once per record, immediately after extraction. Sources don't have to remember to validate; sinks don't have to defensively validate input. The boundary is enforced exactly once.
- **Testability is high.** `IngestionPipelineTests` can substitute fake sources and sinks and assert on the orchestration in isolation. Source tests don't need a sink. Sink tests don't need a source.
- **No HTTP surface to secure.** No auth scheme, no rate limiting, no request validation, no CORS. Less code, less attack surface. For a single-tenant connector this is a net positive.
- **The worker shape ports cleanly to Quartz.** A Quartz `IJob` would call `pipeline.RunAsync(source, sink, context)` with a per-source schedule. The work today (`foreach` in `BackgroundService`) becomes the per-job body; nothing else changes.

### Negative

- **No external trigger.** A human cannot kick off a sync via curl. For a multi-tenant SaaS this would be a real gap; for a single-operator connector with cron-like cadence it isn't.
- **Source extension requires recompiling and deploying.** A controller-based design could load source DLLs at runtime via plugin discovery. For this project's scope that flexibility is not worth the complexity.
- **Operability via logs only.** Without HTTP endpoints there is no `/health` or `/metrics` endpoint. Mitigated by structured logs with correlation IDs (every ingestion run is traceable end-to-end), but a future production deployment would want to add at least a minimal health endpoint.

## Alternatives considered

### Hybrid (pipeline core + thin HTTP adapter)

A pipeline-based core with an optional Web API project that exposes endpoints over the same `IIngestionPipeline`. This is the right design if the connector ever needs to be triggered by something outside its own process — a webhook, an admin UI, an automation tool. It was deferred rather than rejected: the abstractions don't change, so adding the HTTP layer later is purely additive.

### Pure controller-based

Rejected. Either the controller becomes a thin shell that calls a pipeline (in which case we already have the pipeline abstraction, why hide it behind HTTP?), or the controller fattens up with batching and validation logic (in which case adding a second sink means duplicating it across two controllers).