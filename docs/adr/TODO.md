# Known limitations and follow-up work

This document captures the things that are intentionally out of scope for this submission, along with where they would slot in if the project continued.

## Scheduling

The `DataboxConnector.Scheduling` project skeleton exists in the solution but the worker currently runs every source once at startup and exits. Adding Quartz on top of the existing pipeline is a roughly 90-minute change; see [ADR-0003](adr/0003-scheduling-strategy.md) for the rationale and concrete follow-up steps.

## Watermark persistence

`ExtractionContext.From` exists in the API but no component currently persists the last-successful-run timestamp per source. Each run uses `DefaultLookbackDays` from configuration. Combined with primary keys on every dataset, Databox de-duplicates on its side, so the worst case is "we re-send records the API ignores," not "we miss records." A small `IWatermarkStore` (one row per source, last successful timestamp) would close the loop and reduce ingestion cost.

## Production secret storage

`FileBasedSpotifyTokenStore` and `FileBasedIdentifierStore` write to `data/*.json`. Both interfaces (`ISpotifyTokenStore`, `IDataboxIdentifierStore`) exist precisely so a production deployment can swap the file implementation for Azure Key Vault or AWS Secrets Manager without touching the rest of the codebase.

## Health and metrics endpoints

The connector has no HTTP surface today (see [ADR-0001](adr/0001-pipeline-architecture.md)). For a production deployment a minimal Kestrel host with `/health/live`, `/health/ready`, and `/metrics` (Prometheus format) would let an orchestrator restart it on failure and surface ingestion lag as an alertable metric.

## Spotify connector test coverage

GitHub source and Databox sink each have ~40 tests covering pagination, error mapping, and every mapper branch. Spotify has ~41 tests covering the OAuth refresh logic in detail (including a 10-task concurrent refresh test) but lighter coverage of the API client itself. The asymmetry reflects time pressure during the submission window — the OAuth pieces were the highest-risk and got the most attention. Equalizing the test coverage is straightforward follow-up.

## End-to-end integration tests

There is no test project that runs the full pipeline against a recorded HTTP fixture. `MockHttpMessageHandler`-based tests exist per component but a single test that exercises `Pipeline → Source → Sink` with canned responses on both ends would catch wiring regressions that unit tests miss.

## Schema migration tooling

Datasets are versioned (`github_commits_v1`). When `_v2` becomes necessary, the practical procedure today is "create the new dataset, point new dashboards at it, retire the old one once nothing reads it." This is fine but undocumented; a short runbook (or, better, an `IDataset.Deprecate()` hook on the schema) would make the migration story explicit.