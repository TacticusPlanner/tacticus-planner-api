## Context

`Profile.DisplayName` is already required. `GetCurrentUserEndpoint` provisions it from `name` or `preferred_username`, `/me` returns it, and `GetUserJotTokenEndpoint` passes it to `UserJotTokenSigner`. The V1 import request carries the submitted username, while the retrieved V1 profile has no canonical username field. See paired apps change `establish-app-owned-display-name`.

## Goals / Non-Goals

**Goals:** Make confirmation explicit and use only a confirmed application name in public identity; preserve private suggestions for onboarding.

**Non-Goals:** Changing Google/Entra identity, requiring globally unique names, or modifying the V1 production repository.

## Decisions

1. Keep the existing `Profile.DisplayName` column as the stored value/suggestion and add nullable `DisplayNameConfirmedAt` as provenance. A null timestamp means unconfirmed. Alternative: infer confirmation from non-empty/name pattern. Rejected because existing provider values may look valid yet were never chosen.
2. Backfill existing profiles with null confirmation. `/me` returns confirmed `displayName` separately from private `suggestedDisplayName`; the client must not treat the latter as public identity. The name-update endpoint sets both value and timestamp atomically for the authenticated profile. Alternative: let old `/me` field continue carrying provider data. Rejected because it would perpetuate ambiguous ownership.
3. On successful V1 login and profile retrieval, use the submitted trimmed username as an unconfirmed suggestion only if the profile has not been confirmed. Do not infer a canonical username from the V1 profile response. Alternative: auto-confirm the login string. Rejected because a login identifier can be email-like and the user has not agreed to publish it.
4. UserJot gets confirmed name or a fixed `Planner User` fallback; it never falls back to claim or suggestion. This is a server-side privacy boundary, not just a widget display choice. `/me` and `PUT /me/display-name` are the contract shared with the apps companion.

## Risks / Trade-offs

- [Existing users see a one-time name prompt] → Preserve the old value as an editable suggestion and backfill no automatic confirmations.
- [API/apps deployment skew around nullable `/me` name] → Apply API first and deploy the companion client in coordination; the V2 contract is intentionally breaking.
- [A user explicitly chooses an email-like name] → The name is user-authored and editable; prevent accidental provider fallback, not deliberate naming choices.

## Migration Plan

Add the nullable timestamp in the same API change and leave existing rows null. New provisioning writes an unconfirmed suggestion; first confirmation sets the timestamp. The migration does not discard existing name text. Rollback of code does not reverse the column or rewrite names; if necessary, redeploy the prior API only in a coordinated pre-production rollback.

## Open Questions

- Which UserJot integration tests currently assert the exact fallback label? Update those assertions during implementation without changing the generic-fallback requirement.
