# ADR-0003: Run-once-on-startup before adding a scheduler

**Status:** Accepted
**Date:** 2026-04-29

## Context

The challenge brief implies repeated execution — a connector that runs on a cadence, ingests fresh data, and updates the dashboard. The natural way to implement this in .NET is Quartz.NET (or the lighter `BackgroundService`-with-`PeriodicTimer` approach), with one job per source and a configurable cron expression.

The project includes a `DataboxConnector.Scheduling` project skeleton intended to host this. Yet at submission time the worker runs every source **once at startup** and then stops the host. This ADR explains why and what changes when the scheduler is added.

## Decision

**Defer the scheduler until the rest of the pipeline is verified end-to-end against the live Databox API.**

The current worker implementation:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    foreach (var source in _sources)
    {
        if (stoppingToken.IsCancellationRequested) break;
        var result = await _pipeline.RunAsync(source, _sink, new ExtractionContext(), stoppingToken);
        // ...log result
    }
    _lifetime.StopApplication();
}
```

iterates `IEnumerable<ISourceConnector>` once, runs the pipeline per source, and calls `IHostApplicationLifetime.StopApplication()` on completion.

## Reasoning

The decision was made by ranking risks. The highest-risk part of the project was always going to be the **first real call to the Databox Ingestion API**. Several assumptions had to be validated against actual responses:

- the response shapes (the `id` field returning as a JSON number rather than a string was discovered this way and required a custom converter)
- the `dataSourceId` typing in `POST /v1/datasets`
- the resilience handler validation rules (`SamplingDuration` vs `AttemptTimeout`)
- the `Authorization: Bearer` header attachment for Spotify (was missing entirely until end-to-end testing surfaced it)
- file path resolution against the working directory chosen by `dotnet run --project ...`

Every one of these was fixable in a few minutes. None of them were predictable from the documentation alone. **And every one of them would have been hidden behind a 60-minute scheduler tick** if I had built the scheduler before validating the pipeline.

A scheduler also adds questions that are only meaningful once the pipeline works:

- Per-source cron expressions, or one global cadence?
- Misfire handling — what does the job do if a tick is missed because the host was down?
- Overlap protection — what if the previous run is still extracting when the next tick arrives?
- Jitter — how do we avoid hitting the GitHub API on every connector instance simultaneously at `:00`?

These are real questions and Quartz answers them well. They are also questions whose right answer depends on the operational characteristics of the working pipeline. Designing them upfront would have been speculation; designing them now will be informed by what actually happens when the connector runs.

## Consequences

### Positive

- **The integration was validated quickly.** End-to-end Databox ingestion was working within hours of starting the host wireup, which gave time to fix the surprises (number-or-string IDs, missing auth header) before the submission deadline.
- **The worker shape ports cleanly.** A Quartz `IJob` would call `pipeline.RunAsync(source, sink, context)` with one source per job. The `IEnumerable<ISourceConnector>` iteration in today's worker becomes the discovery mechanism the scheduler uses to register jobs.
- **The dashboard demo is live.** The Databox databoard linked from the README contains real data from a real run, not mocked or seeded fixtures.

### Negative

- **The submitted connector is not "always-on."** Reviewers running it locally see one batch of data per `dotnet run`. The dashboard shows current data because I ran it on submission day; it will become stale until the next manual run.
- **The scheduler is a known gap.** The `DataboxConnector.Scheduling` project exists in the solution but is empty.

The negative consequences are time-bounded: adding Quartz is a clearly-scoped follow-up that takes about 90 minutes given the existing abstractions. The positive consequences (validating the integration before the deadline) are not recoverable if the order is reversed.

## Follow-up scope (post-submission)

The work to add Quartz is roughly:

1. Configure `IServiceCollection.AddQuartz(...)` and `AddQuartzHostedService(...)` in the host.
2. Create one `IJob` per `ISourceConnector` that resolves the source, the sink, and the pipeline from DI and calls `RunAsync`.
3. Add `appsettings.json` cron expressions per source (commits hourly, top tracks daily — the cadences differ).
4. Configure misfire policy (`DoNothing` is appropriate for ingestion; rerun the next tick) and `DisallowConcurrentExecution`.
5. Replace the `Worker.ExecuteAsync` foreach with a no-op (or remove `Worker` entirely; Quartz's hosted service takes over).

## Alternatives considered

### Quartz first, validate later

Rejected. See "Reasoning" above — every fixable issue surfaced during integration would have been hidden by a delayed schedule.

### `PeriodicTimer` instead of Quartz

Considered. `PeriodicTimer` is in the BCL, has zero dependencies, and is sufficient for "every N seconds" loops. It does not, however, support cron expressions, per-source cadences, misfire handling, or overlap protection without extra code. Quartz is worth its dependency cost the moment you have more than one source running on different cadences, which is the case here.

### Trigger from outside (cron + `dotnet run`)

A cron line on the host machine could call `dotnet run --project src/DataboxConnector.Host` periodically. This works, costs no in-process scheduler, and matches how some CI pipelines run jobs. It was rejected because it makes overlap protection, structured logging across runs, and graceful shutdown harder, and because the connector's natural deployment shape (a containerized worker) wants a long-running process.