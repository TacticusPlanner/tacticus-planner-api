## 1. Configuration

- [x] 1.1 Add `UserJot:ProjectId` / `UserJot:ProjectSecret` keys (empty placeholders in source-controlled `appsettings.json`, real values per environment) and a bound options type; verify the app starts and the options bind without error.

## 2. Token endpoint

- [x] 2.1 Implement an authenticated endpoint that signs an HS256 JWT with `sub`, `iss`, `aud`, `iat`, and `exp` (<= 1 hour after `iat`); verify a test asserts all five claims on a successful call.
- [x] 2.2 Include a `firstName` claim carrying the caller's display name; never include email. (Amended after live verification: the original plan derived `firstName`/`lastName` from `given_name`/`family_name` token claims and included `email` — the identity provider in use doesn't populate those name claims, which surfaced as "Unknown" in the UserJot widget, and the user asked to drop email for privacy. Now reads `Profile.DisplayName` from the DB, the same value the app itself shows, and never sends email.) Verify tests cover both the default-name and a custom-display-name case, and assert no `email`/`lastName` claim is ever present.
- [x] 2.3 Reject unauthenticated requests without issuing a token; verify a test asserts an auth error and no token in the response.
- [x] 2.4 Verify (with a test) that a successful response body contains only the signed token and nothing derived from the raw project secret.
- [x] 2.5 Verify two consecutive calls for the same user return distinct tokens with independent `iat`/`exp` (per spec: "Each call issues a fresh token").

## 3. Contract

- [x] 3.1 Verify the regenerated `artifacts/openapi` artifact includes the new endpoint after a build.
- [x] 3.2 Note the finalized endpoint path and request/response shape for the companion `tacticus-planner-apps` change (`add-userjot-widget`), which calls it.

Endpoint: `GET /api/v1/me/userjot-token` (authenticated, no request body) -> `200 OK` with `{ "token": "<signed JWT>" }`; `401` if unauthenticated; `404` if the caller has no planner account yet (call `/api/v1/me` first to provision one, per the existing convention).

## 4. Gates

- [x] 4.1 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`.
- [x] 4.2 Run `dotnet build TacticusPlanner.slnx -c Release --no-restore`.
- [x] 4.3 Run `dotnet test TacticusPlanner.slnx -c Release --no-build`.
