# Proposal

## Why

The apps client hand-writes every request and response type (about 1,200 lines under `entities/*/api`), so each contract change is found by hand. The rebuild-foundation-pre-launch change showed how costly that is. The API already commits its OpenAPI document (`artifacts/openapi/TacticusPlanner.Api.json`), but the document doesn't generate usable types yet:

- Schema names are fully qualified CLR names, e.g. `TacticusPlannerApiFeaturesGoalsGoalDetailResponse` or `SystemCollectionsGenericIReadOnlyDictionaryOf…`.
- Fields with a fixed set of values (`status`, `goalType`, `entityType`, `farmingStrategy`, …) are typed as plain `string`, because the DTOs carry `enum.ToString()` and endpoints parse the strings by hand with `Enum.TryParse` (18 places).

This is the API half of the cross-repo pair `generate-api-client-types`. The apps half (same name in `tacticus-planner-apps`) generates client types from this document and applies after it.

## What Changes

- The OpenAPI document gives every component schema a short, unique name: the type name without its namespace, with generic arguments spelled out briefly. For example `GoalDetailResponse`, or `GameCatalogDatasetEnvelopeOfCharacterView` instead of the full namespace chain.
- Request and response fields whose values come from a fixed set are typed as their enums. The document then lists the allowed values (`enum: [Active, Paused, Completed, Archived]`).
  - Wire values stay byte-identical: today's `enum.ToString()` PascalCase strings.
  - Requests keep accepting values case-insensitively.
  - An unknown value is still a 400 `validation` problem with the field named in `errors`.
- The hand-written `Enum.TryParse` checks and the validator rules that duplicate them are removed.
- A test fails the build when two schemas would get the same short name, or when a fixed-value field is typed as a plain string in the document.
- **BREAKING (document only):** schema names in `artifacts/openapi/TacticusPlanner.Api.json` change. Nothing consumes them by name today. JSON on the wire is unchanged.
- **BREAKING (routes):** the `me/` prefix is dropped. Profile-owned resources sit at the top of `/api/v1` (`goals`, `projects`, `legendary-event-plans`, `player-data`, `player-data-overrides/*`, `settings` (was `me/user-settings`), `userjot-token`), account operations move under `account` (`GET`/`DELETE /account`, `account/display-name`, `account/tacticus-integration`, `account/v1-import`), and `guilds/me[...]` plus `guilds/register` become `guild[...]` and `guild/register`. `game-catalog` routes are unchanged. The only client is the apps companion change, which migrates in the same release.

## Capabilities

### New Capabilities
- `openapi-contract`: the API's published OpenAPI document as a code-generation contract. It covers short and unique schema names, fixed-value fields declared as string enums, and the document being regenerated and committed with every contract change.

### Modified Capabilities
<!-- None: no endpoint's request/response behavior changes. -->

## Impact

- **Code:**
  - `src/TacticusPlanner.Api/Program.cs`: OpenAPI options and the JSON enum converter.
  - Response and request records in `Features/**`. Fields go from `string` to enum types, mainly in Goals, Projects and LegendaryEventPlans.
  - The mappers that call `.ToString()`, and the endpoints and validators that call `Enum.TryParse`.
- **Artifact:** `artifacts/openapi/TacticusPlanner.Api.json` is regenerated: renamed schemas, plus `enum` lists added.
- **Tests:** a new OpenAPI document test. The existing endpoint tests and Verify snapshots must still pass unchanged, which proves the wire format didn't change.
- **Consumers:** `tacticus-planner-apps` gets no runtime change. Its companion change starts consuming the document.
