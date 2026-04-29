# Architecture

This document explains the design of the Databox Connector — the layering, the abstractions, why things are structured the way they are, and where the most important trade-offs sit. For implementation details see the source; for the highest-stakes decisions see the [ADRs](adr/).

---

## 1. Goals and constraints

The challenge brief asks for a service that pulls data from external sources and pushes it to the Databox Ingestion API. Beyond the literal requirement, I treated the project as if it were the seed of a real production connector platform that needs to:

- accommodate **multiple sources** (GitHub, Spotify, and easily a third or fourth) without each one re-implementing batching, validation, retries, or scheduling;
- treat the **dataset schema as a first-class artifact** so that a downstream dashboard contract is explicit, versioned, and validated at the boundary;
- isolate **secrets** (API keys, OAuth tokens) from source control and from each other;
- be **observable enough to debug remotely** — every log line is correlated to a single ingestion run;
- fail loudly and meaningfully when the upstream API changes shape, rather than silently inserting garbage.

The non-goals were equally important: this is **not** a generic ETL engine, **not** a backfill tool, and **not** a multi-tenant SaaS. Decisions were made with a single-tenant, hourly-cadence connector in mind.

---

## 2. The pipeline

The whole system is a single sentence: **a pipeline runs a source against a sink, in a context.**

```
                ┌──────────────────────────┐
                │     IIngestionPipeline    │
                │                           │
   ISource ────►│  RunAsync(source, sink,   │────► ISink
                │           context)        │
                └──────────────────────────┘
```

Three abstractions in `DataboxConnector.Core` define the contract:

```csharp
public interface ISourceConnector
{
    string SourceName { get; }
    DatasetSchema Schema { get; }
    IAsyncEnumerable<RawRecord> ExtractAsync(ExtractionContext ctx, CancellationToken ct);
}

public interface ISinkConnector
{
    string SinkName { get; }
    Task EnsureReadyAsync(DatasetSchema schema, CancellationToken ct);
    Task IngestBatchAsync(DatasetSchema schema, IReadOnlyList<RawRecord> batch, CancellationToken ct);
}

public interface IIngestionPipeline
{
    Task<IngestionResult> RunAsync(
        ISourceConnector source,
        ISinkConnector sink,
        ExtractionContext context,
        CancellationToken cancellationToken);
}
```

The pipeline does the boring orchestration: pull records from the source as an `IAsyncEnumerable<RawRecord>`, validate each one against the schema, accumulate them into batches of 100 (the Databox Ingestion API limit), call the sink for each batch, count, log, return a result.

Sources and sinks know nothing about each other and nothing about the pipeline. Adding a third source means writing one class that implements `ISourceConnector` and one DI registration extension. Adding a third sink — a database, a queue, an S3 bucket — means the same on the sink side. The pipeline does not change.

This is also why the `ExtractionContext` exists rather than the source reading "since when?" out of configuration directly: the pipeline can supply it, the sink can supply it (e.g. last successful watermark), or a future scheduler can supply it. The source treats the cutoff as data, not configuration.

For the rationale behind choosing pipeline orchestration over a controller/endpoint model, see [ADR-0001: Pipeline-based architecture](adr/0001-pipeline-architecture.md).

---

## 3. Schema as a first-class artifact

A `DatasetSchema` is not documentation. It is an executable contract:

```csharp
public sealed record DatasetSchema(string Key, string Title, IReadOnlyList<FieldDefinition> Fields)
{
    public IReadOnlyList<string> PrimaryKeys { get; }      // computed at construction
    // duplicate field names, empty key/title, etc. throw at construction time
}

public sealed record FieldDefinition
{
    public required string Name { get; init; }
    public required FieldType Type { get; init; }    // Integer, Decimal, String, Boolean, DateTime
    public bool IsPrimaryKey { get; init; }
    public bool IsNullable { get; init; }
    public string? Description { get; init; }
}
```

Each source exposes its schema as a `static readonly DatasetSchema Instance` — see `Schema/GitHubCommitsSchema.cs` and friends. The schema is what the sink uses to provision the dataset in Databox (title, primary keys), and what `SchemaValidator` checks every record against at the pipeline boundary.

The validator is strict on purpose: a missing required field, a `null` in a non-nullable column, a string where an integer is declared — all of these throw rather than getting silently dropped or rounded. The cost of a noisy failure on a malformed upstream payload is much lower than the cost of a quietly wrong dashboard.

Schemas are **versioned**: the dataset key (`github_commits_v1`, `spotify_top_tracks_v1`, etc.) carries a `_v1` suffix. When a breaking change is needed, the v2 schema becomes a new dataset rather than a destructive migration of the existing one. Existing dashboards keep working; new dashboards opt in.

---

## 4. The two source connectors

Both sources implement `ISourceConnector` but the access patterns are very different.

### 4.1 GitHub — synchronous PAT, page-based

GitHub API uses a Personal Access Token in `Authorization: Bearer ...`. Pagination is via the RFC 5988 `Link` header — `LinkHeaderParser` walks `rel="next"` until the chain runs out, which matches GitHub's own guidance better than incrementing `?page=N` blindly. The API also has surprising shape mismatches between endpoints:

- `/repos/{owner}/{repo}/commits` does **not** return `additions`/`deletions` in the listing payload. To get them, the source has to fetch each commit individually (`GET /commits/{sha}`). That's a 100x rate-limit cost, so it's gated behind `IncludeCommitStats` (default `false`).
- `/repos/{owner}/{repo}/pulls` does **not** accept a `since` query parameter, even though the analogous commits endpoint does. The source compensates by sorting `updated desc` and stopping iteration when it sees a PR older than the requested cutoff. It is documented inline so the next reader doesn't lose 30 minutes to "why is this loop weird?"

Errors are mapped specifically: 401/403 surfaces as "auth — check the PAT," 404 surfaces as "repo missing or no permission," and everything else falls through to a generic message with the body. An operator reading the log knows exactly what to do.

### 4.2 Spotify — OAuth 2.0 with PKCE

Spotify is meaningfully more complex because the connector has no UI, and yet the OAuth flow requires a browser redirect and user consent. Three pieces solve this:

```
   ┌──────────────────────────────────┐
   │ DataboxConnector.Tools.SpotifyAuth│   one-time, manual,
   │                                  │   run by the operator
   │ • Opens browser to /authorize    │
   │ • Listens on 127.0.0.1:8888      │
   │ • Exchanges code for tokens      │
   │ • Writes tokens.json             │
   └──────────┬───────────────────────┘
              │ writes
              ▼
        ┌──────────────┐
        │ tokens.json  │  (gitignored)
        └──────┬───────┘
              │ reads
              ▼
   ┌──────────────────────────────────┐
   │  DataboxConnector.Host (worker)  │   automated,
   │                                  │   no human in the loop
   │ • Loads tokens at startup        │
   │ • Refreshes when expired         │
   │ • Persists rotated refresh token │
   └──────────────────────────────────┘
```

Inside the running connector, three layers handle tokens:

- `ISpotifyTokenStore` — abstracts persistence (file today, Key Vault tomorrow);
- `ISpotifyTokenProvider` — caches the access token in memory, refreshes when within a 60s margin of expiry, **double-checked-locked** so concurrent jobs trigger one refresh rather than racing;
- `SpotifyApiClient` — calls `tokenProvider.GetAccessTokenAsync()` per request and attaches `Authorization: Bearer …` to a freshly-constructed `HttpRequestMessage` (rather than `HttpClient.DefaultRequestHeaders`, since the value rotates).

The bootstrap CLI uses **Authorization Code Flow with PKCE** (`code_verifier` + S256 `code_challenge`), not the client-secret flow. PKCE is what Spotify itself recommends for desktop clients, and it removes the "leak the secret to the local machine" problem. The `state` parameter is validated on the callback for CSRF protection.

For the rationale behind the bootstrap-CLI / runtime-store split, see [ADR-0002: OAuth token storage and refresh](adr/0002-oauth-token-storage.md).

---

## 5. The Databox sink

`DataboxSink` wraps three Ingestion API endpoints behind a small, internal `IDataboxApiClient`:

```
POST /v1/data-sources                       # create the data source (one per connector instance)
POST /v1/datasets                           # create one dataset per schema
POST /v1/datasets/{datasetId}/data          # ingest a batch of up to 100 records
```

The data-source ID and per-dataset IDs are issued by Databox on first creation. Re-creating them on every startup would be wasteful and would also detach old dashboards from new datasets, so the sink writes them to a small local file (`data/databox-identifiers.json`) on first creation and reads them back on subsequent runs. The store is behind `IDataboxIdentifierStore`; in production the file would be replaced with a database or a managed key-value store, transparently.

Two things deserve a footnote because they bit me during integration:

1. **Identifiers come back as numbers in some endpoints.** `POST /v1/data-sources` returns `{"id": 4958311}` — a JSON number. My initial DTOs typed the field as `string?` and the deserializer threw. Fix: a custom `JsonNumberOrStringToStringConverter` that reads either a number or a string and exposes it as `string?` on the C# side. The converter is applied to the `Id` and `IngestionId` fields of every Databox response DTO. The C# side stays uniform regardless of what the wire actually carries, and the converter is robust to either direction the API drifts in future.

2. **Standard resilience handler validation.** `AddStandardResilienceHandler` enforces `SamplingDuration > 2 × AttemptTimeout` and `TotalRequestTimeout > AttemptTimeout`. With my initial values (`AttemptTimeout=15s`, default `SamplingDuration=30s`) the constraint failed by exact-match. The fix was to settle on a single configuration shared by all three HTTP clients: `AttemptTimeout=10s`, `SamplingDuration=30s`, `TotalRequestTimeout=60s`. I lift this into ADR territory because it's the kind of decision that is invisible in the code unless you've hit it.

Errors from the sink surface a structured `DataboxErrorEnvelope` (when present in the response body) rather than a raw HTTP status — so an operator sees `code=invalid_field, field=primaryKeys, …` rather than `400 Bad Request`.

---

## 6. The host: DI, configuration, observability

`DataboxConnector.Host` is a worker service that boots Serilog, builds DI, and runs the worker. The worker iterates `IEnumerable<ISourceConnector>` and runs one pipeline per source against the single registered `ISinkConnector` (Databox). Once all sources finish, it calls `IHostApplicationLifetime.StopApplication()` and exits — there is no scheduler in this iteration, see §8.

### 6.1 DI registrations are factory-based

Source and sink concrete classes have `internal` constructors. The point is that an external caller cannot bypass the DI extension and new them up directly — there is one wiring path, and the DI extension owns it. Microsoft.Extensions.DI's `ActivatorUtilities`, however, will refuse to call an internal constructor; the symptom is "no suitable constructor found" at startup.

The fix that keeps both properties — encapsulation and DI compatibility — is to register a **factory delegate** inside the same assembly as the type. The DI extension has access to internal types in its own assembly:

```csharp
services.AddSingleton<ISourceConnector>(sp => new GitHubCommitsSource(
    sp.GetRequiredService<IGitHubApiClient>(),
    sp.GetRequiredService<IOptions<GitHubOptions>>(),
    sp.GetRequiredService<ILogger<GitHubCommitsSource>>()));
```

This is the same pattern Microsoft uses internally in `Microsoft.Extensions.Http.HttpClientFactory`.

### 6.2 Configuration

Three layers, in priority order:

1. **`appsettings.json`** ships in the repo with non-secret defaults (base URLs, time windows, page sizes).
2. **User Secrets** in development hold the API keys, PAT, OAuth client credentials, and (optionally) absolute file paths.
3. **Environment variables** override anything in production.

`SpotifyOptions`, `GitHubOptions`, and `DataboxOptions` are bound with `[Required]`, `[Range]`, `[Url]`, `[MinLength]` data annotations and `ValidateOnStart()`. The host either boots with valid configuration or fails immediately with a clear message — no runtime surprises.

### 6.3 Observability

Serilog reads its level configuration from `appsettings.json` and writes both to the console (for local dev) and to a daily-rolling file (for any longer-running deployment). Every pipeline run opens an `ILogger.BeginScope` with `CorrelationId`, `Source`, `Sink`, and `DatasetKey`, and Serilog's `Enrich.FromLogContext` propagates those fields into the log line.

The practical effect: a correlation ID logged once at the start of an ingestion threads through every HTTP request, every retry attempt, and every sink call. A failed Spotify run can be reconstructed end-to-end from the log file with one `grep`.

---

## 7. Testing

There are roughly 150 tests across the four test projects. The split is deliberate:

| Project | Coverage emphasis |
|---|---|
| `Core.Tests` | schema validator branches, batching boundary cases, pipeline failure paths, every property of `RawRecord` and `DatasetSchema` |
| `Sinks.Databox.Tests` | API client request shape, response parsing including the number-or-string converter, identifier store roundtripping |
| `Sources.GitHub.Tests` | `LinkHeaderParser` edge cases, every mapper branch, paginated extraction, multi-repo iteration, `MaxItemsPerRun` cap |
| `Sources.Spotify.Tests` | OAuth token refresh including the **10-task concurrent refresh hits the network exactly once** test, mappers, file-store corruption resilience |

Two testing decisions worth noting:

- **Hand-written test fakes** rather than NSubstitute for source connector tests. NSubstitute's `Returns()` overload resolution treats `IAsyncEnumerable<T>` as awaitable and looks for a non-existent `GetAwaiter`, which makes the standard mock setup unworkable. A small purpose-built fake is also easier to read for the assertions we actually want (call sequence, captured arguments, queued responses).
- **`MockHttpMessageHandler`** (Richard Szalay) for HTTP-level tests. It is a single-purpose tool that lets the test set up exact request matching and returns canned responses without crossing a real network boundary.

---

## 8. What is intentionally not here

A reviewer could reasonably ask why a few things are missing or stubbed.

### 8.1 Quartz scheduling

The `DataboxConnector.Scheduling` project is in the solution but the worker currently runs every source **once at startup** rather than on a cron. The reasoning is sequencing: the highest-risk part of the project was always going to be the live integration with Databox (do my schema assumptions match the API? does identifier persistence work?), and a scheduler that runs the same pipeline I had not yet validated end-to-end would have hidden those problems behind a 60-minute-tick delay.

The shape the worker takes today — `IEnumerable<ISourceConnector>` iterated by a `BackgroundService` — is exactly what a Quartz job consumer would also iterate, so adding `IScheduler` + per-source `IJob` is mechanical follow-up work rather than a redesign. Per-source cron expressions, jitter, and overlap protection live naturally in `appsettings.json` once the scheduler is in.

### 8.2 Backfill / explicit watermark store

`ExtractionContext` has a `From` field, but nothing currently persists the last successful run's watermark. Each run uses `DefaultLookbackDays` instead. Combined with the dataset primary keys (commits keyed on `sha`, plays keyed on `played_at + track_id`, etc.), Databox de-duplicates on its side, so the worst case is "we re-send some records the API ignores," not "we miss records." For a production deployment a small watermark store (one row per source, last successful timestamp) would close the loop — see ADR-0003 for the rationale of the current trade-off.

### 8.3 Spotify token storage in a real secrets manager

`FileBasedSpotifyTokenStore` writes `data/spotify-tokens.json`. That is fine for a developer machine and unacceptable for production. The whole reason `ISpotifyTokenStore` is an abstraction is to make the swap to Azure Key Vault or AWS Secrets Manager a single registration change. Same story for `IDataboxIdentifierStore`.

### 8.4 The dashboard is built, not generated

The Databox dashboard linked from the README was built in the Databox UI (with help from Databox's AI assist for layout suggestions) rather than provisioned from code. The Ingestion API does not expose dashboard provisioning at the time of writing; if it did, declaring the dashboard alongside the schema would be a natural extension.

---

## 9. Summary of decisions

A reviewer skimming for the "what was decided and why" can read these three ADRs:

- [ADR-0001 — Pipeline-based architecture](adr/0001-pipeline-architecture.md)
- [ADR-0002 — OAuth token storage and refresh](adr/0002-oauth-token-storage.md)
- [ADR-0003 — Run-once-on-startup before scheduling](adr/0003-scheduling-strategy.md)

Everything else in the codebase is a consequence of the abstractions established by these three.