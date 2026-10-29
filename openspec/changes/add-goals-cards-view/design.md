## Context

`user_settings` has one row per profile. Each row has a `Revision` concurrency token and a nested owned-JSON `Settings` value (`UserSettingsData`: `DailyEnergy`, `XpBookRarity`), mapped with `OwnsOne(...).ToJson("settings")`. GET lazily creates a defaults row. PUT replaces the whole `Settings` object under an exact-revision check, and a 409 carries the current settings in the read shape. `xpBookRarity` was added the same way by the archived `surface-goal-farming-guidance`, and this change follows that precedent field for field.

The companion change is `tacticus-planner-apps` `add-goals-cards-view`. The two share the `GET`/`PUT /api/v1/settings` contract (see proposal.md for motivation).

## Goals / Non-Goals

**Goals:** carry one validated `goalsView` through GET and PUT, with a `Cards` default that also covers rows saved before the field existed.

**Non-Goals:**
- No display strings on the server.
- No per-device or per-page view state.
- No change to `dailyEnergy`, `xpBookRarity` or revision semantics.
- No new endpoint.

## Decisions

- **Domain stores a string; the wire uses an enum.** This mirrors `XpBookRarity` exactly:
  - `UserSettingsData` gains `GoalsView` as a string defaulting to `"Cards"`, plus a `SupportedGoalsView` set.
  - The API layer adds a `GoalsView { Cards, Table }` enum, used on `UserSettingsResponse` and, with `[JsonRequired]`, on `UpdateUserSettingsRequest`.
  - A string in the JSON keeps the default explicit when the property is missing. A CLR enum in the domain would materialize a missing property as its zero member, which only happens to be `Cards` here and would quietly break if the enum were reordered.
  - Enum binding makes an unsupported value a 400 automatically, case-insensitive with PascalCase output, as the `openapi-contract` enum convention requires.
- **Default on read, no backfill.** `UserSettingsResponse.From` normalizes a missing, null or unsupported stored value to `Cards`, the same way `NormalizeXpBookRarity` does. Existing rows need no data migration.
- **PUT requires the field.** PUT replaces `Settings` wholesale. With an optional field, a client that omits it would silently reset the stored choice. V2 is pre-production, so the break is allowed (`tp-destructive-changes-policy`). The apps half merges right after this one.
- **Contract shape:**
  - Response: `{ dailyEnergy: int, xpBookRarity: XpBookRarity, goalsView: GoalsView, revision: long }`.
  - Request: the same fields, with `goalsView` required.
  - The OpenAPI artifact regenerates on build and is reviewed so that only `goalsView` and its enum schema are added.
- **The migration only updates the snapshot.** Adding a property to an owned JSON type changes the EF model snapshot, and the API runs `Migrate()` on startup, so the change carries a generated migration (`AddUserSettingsGoalsView`). Its `Up`/`Down` are expected to contain no DDL and no data change; only the snapshot diff matters. This is the same outcome `AddUserSettingsXpBookRarity` had.

## Risks / Trade-offs

- [EF might materialize a missing JSON property differently than assumed] → A Postgres test writes legacy JSON (with `DailyEnergy` and `XpBookRarity`, without `GoalsView`) and reads it through the GET mapping. The existing `UserSettingsXpBookRarityPostgresTests` is the pattern.
- [The migration might contain unexpected DDL] → Inspect the generated file. Anything beyond the snapshot diff is a defect to raise, not to accept.
- [A cached older client PUTs without `goalsView` and gets a 400] → Accepted. The pair ships API first with apps immediately after, and V2 has no production users.

## Migration Plan

Deploy the API first. Startup applies the migration, which contains no DDL. Existing rows keep working through the read-side default. Rollback is safe, because older code ignores the extra JSON property. Merge the apps PR right after this one, with its OpenAPI pin set to this PR's merge SHA.
