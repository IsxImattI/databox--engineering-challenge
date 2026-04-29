# Databox Engineering Challenge

A .NET 10 connector that extracts data from external sources (GitHub, Spotify) and pushes it to the Databox Ingestion API for visualization and analytics.

📊 **[Live Databoard](https://app.databox.com/datawall/23d0599b1e9c8b575673a5aac8bb21ef3d107ec69f1bb9f)** — view the metrics produced by this connector

---

## What it does

The connector runs as a worker service and orchestrates an **extract → transform → batch → ingest** pipeline against four datasets:

| Source | Dataset | Description |
|---|---|---|
| GitHub | `github_commits_v1` | Commits across configured repositories with author, repo, and churn metadata |
| GitHub | `github_pull_requests_v1` | Pull requests with state, author, and time-to-merge |
| Spotify | `spotify_recently_played_v1` | Recently played tracks (rolling 50 most recent) |
| Spotify | `spotify_top_tracks_v1` | Snapshots of top tracks across short / medium / long term |

Each source connector implements `ISourceConnector`, every record is validated against a versioned `DatasetSchema`, and the Databox sink batches up to 100 records per ingestion request.

For deeper architectural detail, see [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

---

## Quick start

### Prerequisites

- .NET 10 SDK (`10.0.201` or newer)
- A Databox account with an API key
- A GitHub Personal Access Token with `public_repo` scope (or `repo` for private repos)
- A Spotify Developer App with redirect URI set to `http://127.0.0.1:8888/callback`

### 1. Clone and build

```bash
git clone https://github.com/IsxImattI/databox--engineering-challenge.git
cd databox--engineering-challenge
dotnet build
dotnet test
```

### 2. Configure secrets

Secrets are kept out of source control via [User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets). All commands below are run from the repo root:

```bash
# Databox
dotnet user-secrets set "Databox:ApiKey" "<your-databox-api-key>" --project src/DataboxConnector.Host

# GitHub
dotnet user-secrets set "Sources:GitHub:PersonalAccessToken" "<your-github-pat>" --project src/DataboxConnector.Host
dotnet user-secrets set "Sources:GitHub:Repositories:0" "owner/repo-name" --project src/DataboxConnector.Host
dotnet user-secrets set "Sources:GitHub:Repositories:1" "owner/another-repo" --project src/DataboxConnector.Host

# Spotify (also set on the auth bootstrap tool)
dotnet user-secrets set "Sources:Spotify:ClientId" "<your-spotify-client-id>" --project src/DataboxConnector.Host
dotnet user-secrets set "Sources:Spotify:ClientSecret" "<your-spotify-client-secret>" --project src/DataboxConnector.Host

dotnet user-secrets set "Sources:Spotify:ClientId" "<your-spotify-client-id>" --project src/DataboxConnector.Tools.SpotifyAuth
dotnet user-secrets set "Sources:Spotify:ClientSecret" "<your-spotify-client-secret>" --project src/DataboxConnector.Tools.SpotifyAuth
dotnet user-secrets set "Sources:Spotify:RedirectUri" "http://127.0.0.1:8888/callback" --project src/DataboxConnector.Tools.SpotifyAuth
```

### 3. Authorize Spotify (one-time)

The Spotify source uses OAuth 2.0 with PKCE. A separate CLI tool walks you through the browser-based consent flow once and writes the resulting tokens to `data/spotify-tokens.json`:

```bash
dotnet run --project src/DataboxConnector.Tools.SpotifyAuth
```

The tool opens a browser window, listens on `http://127.0.0.1:8888/callback`, exchanges the authorization code for tokens, and persists them. Subsequent runs of the connector refresh them automatically.

### 4. (Optional) Pin file paths to absolute locations

`dotnet run --project ...` resolves the working directory to the project folder rather than the repo root, which can confuse relative file paths. The cleanest fix is to set them explicitly:

```bash
dotnet user-secrets set "Sources:Spotify:TokenStorePath" "<repo-root>/data/spotify-tokens.json" --project src/DataboxConnector.Host
dotnet user-secrets set "Databox:IdentifierStorePath" "<repo-root>/data/databox-identifiers.json" --project src/DataboxConnector.Host
```

### 5. Run

```bash
dotnet run --project src/DataboxConnector.Host
```

The worker iterates all configured sources, runs one ingestion pipeline per source, logs progress with structured fields, and stops the host when all sources have completed. Logs stream to the console and to `logs/databox-connector-{Date}.log`.

Expected output:

```
[INF] === Running source: github_commits ===
[INF] Created Databox data source: id=4958311 title=Databox Connector Demo
[INF] Created Databox dataset: id=30b03042-... title=GitHub Commits
[INF] Extracted 67 commits across 2 repos
[INF] Ingested batch into Databox: dataset=github_commits_v1 count=67
[INF]   ✓ github_commits: extracted=67 sent=67 batches=1 duration=00:00:02.88
...
```

---

## Project layout

```
src/
  DataboxConnector.Core/                  # abstractions, schema, pipeline orchestration
  DataboxConnector.Sinks.Databox/         # Databox Ingestion API client + sink
  DataboxConnector.Sources.GitHub/        # GitHub source (PAT auth)
  DataboxConnector.Sources.Spotify/       # Spotify source (OAuth2 PKCE)
  DataboxConnector.Scheduling/            # Quartz scheduling glue (placeholder for follow-up work)
  DataboxConnector.Host/                  # Worker Service entry point
  DataboxConnector.Tools.SpotifyAuth/     # one-time OAuth bootstrap CLI

tests/
  DataboxConnector.Core.Tests/
  DataboxConnector.Sinks.Databox.Tests/
  DataboxConnector.Sources.GitHub.Tests/
  DataboxConnector.Sources.Spotify.Tests/

docs/
  ARCHITECTURE.md                         # high-level design walkthrough
  adr/                                    # Architecture Decision Records
```

---

## Submission

| Item | Value |
|---|---|
| Repository | https://github.com/IsxImattI/databox--engineering-challenge |
| Databoard share link | https://app.databox.com/datawall/23d0599b1e9c8b575673a5aac8bb21ef3d107ec69f1bb9f |
| Databox account email | matej.gy@gmail.com |

---

## Further reading

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — detailed walkthrough of design decisions, layering, OAuth flow, and trade-offs
- [`docs/adr/`](docs/adr/) — Architecture Decision Records for the most consequential choices