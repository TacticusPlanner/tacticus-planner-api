## Why

Feedback posts on UserJot arrive with no context: every author's traits are empty, and players type their device or build by hand (REQ-235 had to say "Chrome on Android"). Triage then starts with a round of questions. UserJot's signed identity token accepts a flat `traits` claim that it stores on the member and shows beside their posts, so the planner can attach that context itself.

## What Changes

- The UserJot identity token gains a `traits` claim with non-identifying feedback context: `appVersion`, `appMode` (`installed` | `browser`) and `language`, taken from optional query parameters on `GET /api/v1/userjot-token` and echoed only when well formed.
  - `browser` and `os`, derived on the server from the token request's `User-Agent` header and reduced to a browser family with its major version (`Chrome 131`) and an OS family (`Android`). The full header is never stored or forwarded. Anything malformed is dropped, never rejected, so the deployed client that sends none keeps working.
- No email, key, Tacticus id or other contact data is added; the existing "nothing else identifying" rule still holds.

Companion change: `enrich-userjot-feedback` in `tacticus-planner-apps` sends the query parameters and also sets the widget's page context, reply popups, a "Report a bug" entry point, deep links and an in-widget roadmap.

## Capabilities

### Modified Capabilities

- `userjot-identity-token`: the token carries a flat `traits` claim of feedback context.

## Impact

- `Features/UserJot` (endpoint request DTO, `UserJotTraits`, `CoarseUserAgent`, signer) and the generated OpenAPI artifact (three optional query parameters, additive).
- No database change or migration.
