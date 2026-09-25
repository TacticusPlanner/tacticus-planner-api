## Context

`Profile.DisplayName` is already required. `GetCurrentUserEndpoint` provisions it from `name` or `preferred_username`, `/me` returns it, and `GetUserJotTokenEndpoint` passes it to `UserJotTokenSigner`. The V1 import request carries the submitted username, while the retrieved V1 profile has no canonical username field. See paired apps change `establish-app-owned-display-name`.

## Goals / Non-Goals

**Goals:** Use only a name the user chose in public identity; keep provider and V1 values as private prefill suggestions for onboarding.

**Non-Goals:** Changing Google/Entra identity, requiring globally unique names, or modifying the V1 production repository.

## Decisions

1. Reuse the existing required `Profile.DisplayName` column with no schema change. Its empty string means no name has been set; a chosen name is always 1–80 trimmed characters, so `""` cannot collide with one. First-access provisioning stores `""` instead of the provider claim. Alternatives: a confirmation timestamp or flag (provenance for legacy rows, which do not exist in this pre-production project); inferring "chosen" from the value's shape (rejected: a provider value can look valid without having been chosen).
2. `/me` returns `displayName` (the chosen name, or `null` while none is set) and `suggestedDisplayName`, which is read live from the token's `name`/`preferred_username` claim while no name is set and `null` afterwards. There is no separate confirmation flag: a name exists only because the user submitted it. Nothing derived from a provider claim is persisted, so the suggestion is always private and current. `PUT /me/display-name` writes the trimmed value.
3. The submitted V1 username is not stored either. After successful V1 login and profile retrieval, `POST /me/v1-import` returns it as `suggestedDisplayName` in its response, only while no name is set, and the client holds it in memory to prefill the name step. Trade-off: a reload on the name step falls back to the provider suggestion. Alternative: persist it in a new column (rejected as above). Do not infer a canonical username from the V1 profile response, and do not set the login string as the name automatically: it can be email-like.
4. UserJot gets the chosen name or a fixed `Planner User` fallback; it never falls back to a claim or suggestion. Guild sync likewise treats an empty name as no name instead of surfacing a provider value to guildmates. `/me`, `PUT /me/display-name`, and the V1 import response field are the contract shared with the apps companion.

## Risks / Trade-offs

- [Existing dev accounts hold a provider-derived name that now reads as a chosen name] → Pre-production: reset the local database via the `api-migrations` `Reset Database` command; no backfill is written.
- [API/apps deployment skew around nullable `/me` name] → Apply API first and deploy the companion client in coordination; the V2 contract is intentionally breaking.
- [A user explicitly chooses an email-like name] → The name is user-authored and editable; prevent accidental provider fallback, not deliberate naming choices.

## Migration Plan

No schema change and no migration. Deploy the API first, then the client. Local databases created before this change should be reset.

## Open Questions

- Which UserJot integration tests currently assert the exact fallback label? Update those assertions during implementation without changing the generic-fallback requirement.
