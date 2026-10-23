# Design

## Context

- **How the document is produced:** FastEndpoints.OpenApi on top of the Microsoft.AspNetCore.OpenApi pipeline (`Program.cs`). `Microsoft.Extensions.ApiDescription.Server` writes the document at build time into `artifacts/openapi/` (`OpenApiDocumentsDirectory` in the csproj). `ProblemDetailsOpenApi` already post-processes the document with a document transformer.
- **CI:** `_build-api.yml` builds and tests but never checks that the committed document matches the build output, so the document can silently drift today.
- **Enums today:** only a few domain enums (guild raids, `UnitRank`) carry `[JsonConverter(typeof(JsonStringEnumConverter<T>))]` and already appear as string enums. Goals, Projects and LE plans map enums to `string` in their response records (`GoalMapper`: `goal.Status.ToString()`). Their endpoints and validators parse request strings with `Enum.TryParse(..., ignoreCase: true)` (18 places).
- **Wire casing:** PascalCase (`Active`, `Character`), and the apps client's literal unions match it (`GoalStatus = "Active" | …`).

The companion apps change `generate-api-client-types` consumes this document. The requirements are in `specs/openapi-contract/spec.md`.

## Goals / Non-Goals

**Goals:**
- A document that `openapi-typescript` turns into readable types with literal unions.
- A CI failure whenever the committed document is stale.

**Non-Goals:**
- Changing any route, status code, problem shape or JSON value.
- Renaming C# types for nicer names beyond what's needed to resolve collisions.
- Versioning the document or publishing it as a package. The apps change copies it pinned to a commit.

## Decisions

### 1. Short schema names via the schema reference-id hook
Set `OpenApiOptions.CreateSchemaReferenceId` (inside `options.ConfigureOpenApi`) to a function that:
- returns the CLR type's short name for plain types;
- for generics, returns `<Name>Of<Arg>[And<Arg>]` built recursively from short names, e.g. `GameCatalogDatasetEnvelopeOfIReadOnlyListOfCharacterView`;
- falls back to the default for primitives and collections the pipeline inlines.

If FastEndpoints.OpenApi exposes a first-party switch (`ShortSchemaNames`) with the same result, use that instead.

*Alternative:* a document transformer that renames schemas after generation. Rejected: it also has to rewrite every `$ref`, which is the same work the hook avoids.

### 2. Collision guard as a test, not a runtime check
A test in `TacticusPlanner.Api.Tests` generates the document in-process, the same way the integration tests host the app. It does two things:
- Maps every schema back to its CLR type and fails on two types with one name.
- Fails on any property whose CLR type is an enum but whose schema is an unconstrained `string`.

*Alternative:* throw at startup. Rejected: a startup failure in production for a documentation concern is the wrong trade.

### 3. Enum-typed DTO fields with one global converter
Register `JsonStringEnumConverter` (no naming policy) once, in both FastEndpoints' serializer options and the OpenAPI JSON options.
- Response and request records switch fixed-value fields from `string` to their domain enum (`GoalStatus`, `GoalType`, `GoalEntityType`, `FarmingStrategy`, …). The mappers drop `.ToString()`.
- With no naming policy, output equals `ToString()`, so the wire stays PascalCase. System.Text.Json reads enum names case-insensitively, so the current `ignoreCase: true` acceptance is preserved.
- Numeric input (`"status": 2`) must be rejected: set `allowIntegerValues: false`.
- The `Enum.TryParse` branches and duplicate validator `Must` rules are deleted.
- A bad value now fails at request binding. FastEndpoints reports binding failures as a validation problem keyed by the property, the same path `ApiErrorContractTests` already covers for `startPaused: "yes"`.

*Alternative:* keep `string` fields and add `enum` lists to the schemas with an attribute or transformer. Rejected: the parsing stays hand-written in 18 places, and the list can drift from the enum.

**Scope (decided during apply):** closed literal sets without an enum get one too, keeping their current wire strings through `[JsonStringEnumMemberName]`:
- LE lane (`alpha|beta|gamma`), stored as a string in the domain and converted at the boundary
- annotation status (`maybeClear|stopHere`)
- depth source (`estimate|manual`)
- acquisition-source kind
- project target kind (`legendaryEvent`)
- the V1 import outcome statuses

EF conversions are explicit (`HasConversion`), so JSON member names never change what is stored. Open-ended game-data strings stay `string`: campaign-event-progress `type` (a catalog battle type) and guild-raid modifier `type`/`target`.

Snapshot `initialRank`/`initialProgression` were best-effort before (an unparseable value was dropped). They are now typed, so an unknown value is a 400 like any other enum field.

### 4. CI drift guard
Add a step to `_build-api.yml` after Build: `git diff --exit-code -- artifacts/openapi`. The build regenerates the document, so a stale commit fails CI with the diff in the log.

### 5. Routes drop the `me/` prefix

Every authenticated route already acts on the caller's own profile, so `me/` added nothing and made the generated `paths` keys longer. Profile-owned resources move to the top of `/api/v1`; operations on the account itself (read, purge, display name, Tacticus integration, V1 import) group under `account`; the caller's guild is the singular `guild`. `me/user-settings` becomes `settings` (DTO and schema names already say settings). `PlayerDataChunkKeys.RoutePrefix`, which the manifest serves, follows. The upstream V1 `api/users/me` call is not ours and stays. Done here rather than in a later change because the apps client migrates onto generated `paths` in this release anyway, so the rename costs one regeneration.

## Risks / Trade-offs

- **[Risk]** An enum value's `ToString()` differs from what a mapper emitted, e.g. a mapper that lower-cased a value. → Before switching a field, grep its mapper. The existing endpoint and integration tests assert concrete strings and must pass unchanged.
- **[Risk]** Binding errors word things differently from today's validator messages. → The apps client keys on `issueCode` and the field key, never on message text (rebuild change, `api-error-handling`). Accept the wording change.
- **[Risk]** Short names collide across features, e.g. two `ItemResponse` records. → The collision test fails with both types named. Rename the C# record to resolve it.
- **[Trade-off]** The document diff in this change is large, since every schema is renamed. It's a one-time cost and nothing consumes the names yet.

## Migration Plan

Ship before the apps change. No data migration. Rollback is a revert, because the wire format is unchanged and old clients keep working.
