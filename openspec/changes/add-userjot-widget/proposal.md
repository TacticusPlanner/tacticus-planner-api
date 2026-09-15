## Why

Players have no in-app way to submit feedback, see the roadmap, or read changelog updates — GitHub issue [TacticusPlanner/tacticus-planner-apps#45](https://github.com/TacticusPlanner/tacticus-planner-apps/issues/45) tracks setting up UserJot for this. The companion `tacticus-planner-apps` change wires up the UserJot widget and wants it to know who is logged in. UserJot's widget SDK can attach a plain, unsigned identity from the browser, but anything typed into devtools can spoof it — anyone could call the SDK with someone else's user id and attribute feedback or messages to them. UserJot's signed-identity flow closes that: a server-issued, short-lived JWT proves the identity instead of trusting the browser. That server-side signing has to live here, in the API, because it requires a secret that must never reach the client.

## What Changes

- Add a new authenticated endpoint that mints a short-lived (<= 1 hour) HS256-signed JWT for the current user, with the claims UserJot's signed-identity flow requires (`sub`, `iss`, `aud`, `iat`, `exp`, plus `email`/`firstName`/`lastName`).
- Add `UserJot:ProjectId` / `UserJot:ProjectSecret` configuration, following the same per-environment `appsettings.json` pattern already used for `V1Api:FunctionsKey`.
- Out of scope: UserJot Conversations (live chat) is explicitly not part of this change; no workspace features beyond Feedback, Roadmap, and Updates are assumed.

## Capabilities

### New Capabilities
- `userjot-identity-token`: issues a signed, short-lived identity token for the current authenticated user so the UserJot widget can trust who it's talking to.

### Modified Capabilities

(none)

## Impact

- New endpoint (adds to the generated OpenAPI artifact under `artifacts/openapi`) — the companion `add-userjot-widget` change in `tacticus-planner-apps` calls it to obtain the token before calling the widget SDK's `identify()`.
- New configuration secret (`UserJot:ProjectSecret`) added to `appsettings.json`/`appsettings.Production.json`; no database or migration changes.
