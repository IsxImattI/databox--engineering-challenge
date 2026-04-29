# ADR-0002: OAuth token storage and refresh strategy

**Status:** Accepted
**Date:** 2026-04-27

## Context

The Spotify source needs OAuth-authenticated access to a user's listening history and top tracks. Spotify's OAuth flow has two awkward properties from the perspective of a worker service:

1. **Initial authorization is interactive.** The user must visit Spotify's consent page in a browser, click "Allow," and Spotify will redirect to a registered URI with an authorization code. There is no machine-to-machine fallback for the scopes the connector needs (`user-read-recently-played`, `user-top-read`).

2. **Access tokens expire roughly hourly.** Refresh tokens are long-lived but can be revoked (by Spotify or by the user explicitly de-authorizing the app from their account settings).

The worker, by definition, has no UI and runs without a human in the loop. So: how does it get tokens in the first place, and how does it keep them fresh?

## Decision

**Two processes, one shared persistence layer.**

```
┌──────────────────────────────────┐
│ Tools.SpotifyAuth (CLI)          │   one-time, manual, run by the operator
│                                  │
│ • Builds /authorize URL with     │
│   PKCE code_challenge + state    │
│ • Opens system browser           │
│ • Listens on 127.0.0.1:8888      │
│ • Exchanges code → tokens        │
│ • Writes tokens.json             │
└──────────┬───────────────────────┘
           │ writes
           ▼
     ┌──────────────┐
     │ tokens.json  │  (gitignored, behind ISpotifyTokenStore)
     └──────┬───────┘
            │ reads + writes (after refresh)
            ▼
┌──────────────────────────────────┐
│ Host (worker service)            │   automated, runs unattended
│                                  │
│ • Loads tokens at first use      │
│ • Refreshes within 60s of expiry │
│ • Persists rotated refresh token │
└──────────────────────────────────┘
```

### The bootstrap CLI

A separate console project (`DataboxConnector.Tools.SpotifyAuth`) handles the interactive part once. It implements **OAuth 2.0 Authorization Code Flow with PKCE** — `code_verifier` is 32 random bytes base64url-encoded; `code_challenge` is `SHA256(verifier)` base64url-encoded; `state` is 16 random bytes for CSRF protection. The CLI opens the user's default browser and listens on `http://127.0.0.1:8888/callback` (a loopback IP, which is what Spotify allows after their April 2025 redirect URI policy change — `localhost` no longer works).

After the redirect comes back with the code, the CLI validates the `state`, exchanges the code for `{access_token, refresh_token}`, and writes them to disk. The browser tab shows a confirmation page. The CLI exits. This is run once per machine, manually, by whoever sets the connector up.

### The runtime

`SpotifyTokenProvider` (singleton, registered in DI) is the in-process cache:

- It holds `SpotifyTokens` in memory after first load.
- `GetAccessTokenAsync()` returns the cached value if the token is more than 60 seconds away from expiry. The 60-second margin matters: a token that is "valid for 5 more seconds" at the start of an HTTP call may very well be expired by the time the request lands.
- If the cached token is within the margin (or there is no cached token yet), a `SemaphoreSlim`-guarded refresh runs. The lock matters because the worker can have several concurrent jobs, and without it ten concurrent expired-token observations would trigger ten refresh requests instead of one. The implementation is the standard double-checked-locking pattern: re-check the cache after acquiring the lock, in case another caller already refreshed.
- After a successful refresh, the new tokens are persisted via `ISpotifyTokenStore.SaveAsync` so the next process restart sees them.

### The persistence interface

`ISpotifyTokenStore` is intentionally a small interface (`LoadAsync`, `SaveAsync`). Today's implementation (`FileBasedSpotifyTokenStore`) writes JSON to disk. In production it would be replaced by an Azure Key Vault or AWS Secrets Manager-backed implementation; nothing else in the connector would change.

## Consequences

### Positive

- **The runtime never asks for user input.** Once `tokens.json` exists, the worker can run on a cron, in a container, on a build agent, anywhere — no browser, no human.
- **PKCE is more secure than the simpler client-secret flow.** The client secret is not embedded in the bootstrap binary at all (it is supplied at runtime via user secrets, and even if it leaked it cannot be used without the corresponding `code_verifier`). PKCE is also Spotify's own recommendation for desktop/CLI clients.
- **Concurrent jobs do not race.** The double-checked lock guarantees one refresh per expiry, regardless of how many jobs are running.
- **The 60-second margin avoids mid-request expiry.** Without it, a token that is valid at the moment of the call but expires before the response comes back leads to a confusing 401.
- **Token rotation is supported.** Spotify usually omits `refresh_token` in refresh responses (the existing one stays valid), but it can rotate it. The provider preserves the existing refresh token if the response omits one, and persists the new one if it doesn't.

### Negative

- **An operator has to run the CLI.** This is a one-time cost per environment, but it is a real one — a fresh deployment can't bootstrap itself.
- **A single token store is shared between the CLI and the worker.** They have to agree on the path. Today this is configurable (`Sources:Spotify:TokenStorePath`); in production it would be a Key Vault secret name.
- **A revoked refresh token surfaces only at the next API call.** When that happens, the worker's failure message is explicit ("Tokens may be revoked; re-run the SpotifyAuth bootstrap CLI"), but the operator still has to act. There is no way around this — Spotify provides no proactive revocation signal.

## Alternatives considered

### Client credentials flow

Spotify's client credentials flow does not grant access to user-scoped data (`/me/player/recently-played`, `/me/top/tracks`). It was therefore not an option for this challenge.

### Run a permanent web server in the host process

The connector could expose a `/spotify/callback` endpoint and self-trigger reauthorization. This was rejected: it conflates the "automated worker" concern with the "interactive browser flow" concern, requires the worker to be reachable on a public URL, and forces the operator to keep a browser tab open during what is conceptually a one-time operation. The bootstrap CLI is cleaner and matches how production OAuth setup actually works (a one-time operator-run consent).

### Encrypt `tokens.json` at rest with a machine-bound key

I considered using the OS-level Data Protection API to encrypt the token file. It was deferred as out of scope for this challenge — the value of encrypting a file that already lives in a `gitignore`-protected directory on a developer machine is marginal, and a real production deployment would skip the file altogether in favor of a secrets manager. Both `ISpotifyTokenStore` interface and the eventual replacement remain unchanged.