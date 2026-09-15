## Context

See `proposal.md` - Why. UserJot's widget SDK (v3) accepts an unsigned `identify()` call from the browser, or a signed one backed by an HS256 JWT that the SDK forwards as-is. Signing requires the workspace's UserJot project secret (from UserJot Settings -> Login -> Secrets), which per UserJot's docs must never reach the browser. This repo already stores a comparable third-party secret in plain per-environment config (`V1Api:FunctionsKey` in `appsettings.json`/`appsettings.Production.json`), so the same pattern applies here rather than introducing a new secrets mechanism for one value.

Companion change: `tacticus-planner-apps` (`add-userjot-widget`), which calls this endpoint to get a token before calling the widget SDK's `identify({ token })`.

## Goals / Non-Goals

**Goals:**
- Mint a UserJot-compatible signed identity JWT for the current authenticated user, on demand, with no persisted state.

**Non-Goals:**
- No changes to the existing authentication/authorization pipeline - this endpoint sits behind it, it doesn't change it.
- No UserJot Conversations, automatic-login, or single-sign-on setup - out of scope per the proposal.
- No caching or reuse of issued tokens - each call mints a fresh one (see specs - "Each call issues a fresh token").

## Decisions

- **New minimal endpoint, not a shared "third-party tokens" abstraction.** This is the first server-signed third-party token in the API; a generic abstraction for a single caller would be speculative. If a second such integration shows up, generalize then.
- **JWT signing via the existing JWT library already in use for the API's own tokens**, rather than adding a new dependency - HS256 signing is standard library-adjacent functionality most JWT packages already used here provide.
- **Configuration via `UserJot:ProjectId` / `UserJot:ProjectSecret`, mirroring `V1Api`'s shape.** Keeps the same review/rotation story as the existing third-party secret instead of introducing Key Vault or another secrets store for just this value.
- **Stateless issuance.** No database table or migration is needed; every call derives the token from the current request's authenticated user and current time.

## Risks / Trade-offs

- **Secret lives in plaintext per-environment config, same as `V1Api:FunctionsKey`.** -> Accepted as consistent with this repo's existing convention; not a new risk introduced by this change.
- **A 1-hour token lifetime means the frontend must re-fetch periodically for long-lived sessions.** -> Mitigated on the `tacticus-planner-apps` side (re-fetch when the widget is reopened after expiry); no server-side action needed.

## Migration Plan

- Add configuration keys with empty/placeholder values in source-controlled `appsettings.json`, real values supplied per environment the same way `V1Api:FunctionsKey` is today.
- No data migration. Endpoint can ship and be enabled independently of the frontend change, since it has no effect until called.
