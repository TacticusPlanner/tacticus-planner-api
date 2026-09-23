## Why

`ACC-001` exposes a privacy and ownership gap: first-access provisioning copies provider claims into `Profile.DisplayName`, `/me` returns that value, and UserJot publishes it, but users cannot confirm or edit it. Public identity needs to come from a deliberate application profile choice.

## What Changes

- Track whether the existing profile display name has been confirmed; legacy/provider-derived names remain suggestions, not public nicknames.
- Add an authenticated profile-scoped display-name update operation and return confirmation state through `/me`.
- Use the authenticated V1 login username as an unconfirmed initial suggestion only when no name was confirmed; never overwrite an edited name.
- Issue UserJot identity tokens with a confirmed app-owned name only, or a non-identifying fallback until confirmation. **BREAKING:** `/me` adds confirmation/suggestion fields and may return no confirmed display name.

## Capabilities

### New Capabilities

- `profile-display-name`: Storage, validation, confirmation, edit, and V1 suggestion contract for app-owned names.

### Modified Capabilities

- `userjot-identity-token`: Public token name must not use an unconfirmed provider or V1 login value.

## Impact

`Profile`, EF migration/backfill, `/me`, new update endpoint, V1 import flow, UserJot signer endpoint, API tests, regenerated OpenAPI. Paired `tacticus-planner-apps` change `establish-app-owned-display-name` consumes the contract. V2 breaking API evolution is permitted by the workspace policy.
