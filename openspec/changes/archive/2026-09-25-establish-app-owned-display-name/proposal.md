## Why

`ACC-001` exposes a privacy and ownership gap: first-access provisioning copies provider claims into `Profile.DisplayName`, `/me` returns that value, and UserJot publishes it, but users cannot choose or edit it. The project is pre-production with no real users, so no data migration or legacy handling is needed. Public identity needs to come from a deliberate application profile choice.

## What Changes

- `Profile.DisplayName` holds only a name the user chose; an empty value means none has been set yet. Provider claims are read live as a private suggestion and never stored as the name.
- Add an authenticated profile-scoped display-name update operation; `/me` returns the chosen name or `null`.
- Return the authenticated V1 login username from a successful V1 import as a prefill suggestion, only while no name is set; never store it or overwrite a chosen name.
- Issue UserJot identity tokens with the chosen name only, or a non-identifying fallback until one is set. **BREAKING:** `/me` adds a suggestion field and may return no display name.

## Capabilities

### New Capabilities

- `profile-display-name`: Storage, validation, edit, and V1 suggestion contract for app-owned names.

### Modified Capabilities

- `userjot-identity-token`: Public token name must not use a provider claim or V1 login value.

## Impact

`Profile` (no schema change), `/me`, new update endpoint, V1 import flow, UserJot signer endpoint, API tests, regenerated OpenAPI. Paired `tacticus-planner-apps` change `establish-app-owned-display-name` consumes the contract. V2 breaking API evolution is permitted by the workspace policy.
