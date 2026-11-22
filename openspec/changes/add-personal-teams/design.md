## Context

See `proposal.md` for the motivation and the specs for the behavior. Companion change: `add-personal-teams` in `tacticus-planner-apps`, sharing the `/api/v1/teams` endpoints and the V1 import contract. This half applies first.

Current state that shapes the approach:

- V2 has no teams outside Legendary Event plans. ADR 0008 keeps those owned by the plan and expects a separate surface if Guild War needs one. This is that surface.
- ADR 0011 stores an aggregate read and written whole as one row, with invariants in validators rather than CHECK constraints.
- The docs propose both `planning.teams` and `GUILD_WAR_TEAMS`, written before ADR 0011. Neither exists.
- `Project` already shows the pattern for profile-owned rows: a `Revision` concurrency token, a `HasQueryFilter` on `CurrentProfileId`, a 409 carrying the current row, and limits counted under `LockCurrentProfileAsync`.
- `GameCatalogSnapshot` already exposes `Characters`, `Mows`, `CampaignGroups` (with `AlliesFactions` and `CoreCharacters`) and `IncursionViews` (with `AllowedFactions`).

## Goals / Non-Goals

**Goals:**

- One definition of a lineup that War Defense and War Offense can reference by id later.
- One rule evaluator for saves, reads and import, so they cannot disagree.

**Non-Goals:**

- War Defense and War Offense state, War Zones, and sharing teams.
- Moving Legendary Event teams into this model.
- Any catalog dataset, manifest or schema-hash change. No `Data/**` file changes.

## Decisions

### D1: One row per team, with its lineup in columns of that row

A new public `teams` table holds `id`, `profile_id`, `revision`, `name`, `name_key`, `import_key`, `sort_order`, `modes`, `character_ids`, `flex_start_index`, `mow_ids`, `mow_flex_start_index`, `campaign_group_id`, `incursion_mow_ids`, `notes`, `rarity_cap`, `created_at` and `updated_at`. The list columns are `text[]` primitive collections. The lineup is only ever read and written through its team, which matches ADR 0011 rule 1.

Alternative considered: one per-profile row with every team as JSONB, as Legendary Event plans do. Rejected because teams are edited one at a time, so a single revision would make two devices conflict over unrelated teams, and later War Defense references would need application-level integrity. Member tables were rejected by ADR 0011.

### D2: Uniqueness and limits

`name_key` is the trimmed name lowercased with the invariant culture, with a unique index on `(profile_id, name_key)` as the backstop and a pre-check that returns `teamNameTaken`. The team limit (50) is one options value (`TeamOptions.Limit`), as `PersonalLimit` is for roster snapshots, so raising it later is configuration, not a code change. Creates, deletes, reorders and the import run under `LockCurrentProfileAsync`, which keeps the limit and positions correct under concurrency. `sort_order` is stored 1-based and contiguous, is not unique in the database, and is served as `position`.

### D3: One rule evaluator

`TeamRules.Evaluate(team, catalogFacts)` in `TacticusPlanner.Domain` returns the codes a team breaks. It takes plain facts, not the catalog, so the domain keeps no catalog reference. `TeamCatalogFacts` in `Features/Teams` builds those facts from `GameCatalogSnapshot`: unit ids and factions, non-Elite campaign groups, and Incursions by MoW id. Create, update and the read projection use `Evaluate`, and the import uses it to list outcome issues.

### D4: Issues are derived, never stored

`issues` are computed on read. A catalog update can therefore raise or clear them with no data migration, and no stored flag can drift. Reading 50 teams is an in-memory check against a snapshot that is already loaded.

### D5: Campaign uses the base group id

A team stores a catalog group id such as `campaign1` or `eventCampaign3` rather than V1's string keys. Elite is a difficulty of the same storyline, so Elite groups are refused, matching V1. The group's `Types` decide this: a group is Elite when any of its types is Elite or EliteMirror.

### D6: Incursion rules use allowed factions, not V1's alliance

V1 limits characters by a hand-kept `deployableAlliance`. The served `incursions` dataset has the game's own `allowedFactions`, which is stricter: `orksRukkatrukk` allows 6 Imperial factions, not all of them. A team for several Incursions needs every character allowed in all of them. V1's union only worked because its choices all shared one alliance. V1 teams that fall outside this import with `factionNotAllowed`.

### D7: Import reuses the Legendary Event unit resolver

`UnitResolver` and its aliases move out of `V1LegendaryEventImportService` into a shared internal type in `Features/V1Import`, unchanged, so both imports resolve units identically. `V1UserData` gains a raw `Teams2` `JsonElement`, read by `TacticusV1Client.ReadTeams`. A malformed blob therefore fails only this part, as the Legendary Event blobs do. `V1TeamImportService` writes all of its teams in one transaction under the profile lock, then returns outcomes.

### D8: Re-run key is an immutable V1 import key

Imported teams store `import_key`, the original V1 name trimmed and lowercased with the invariant culture, before any shortening. It is null for teams made in V2 and never changes on rename. A partial unique index on `(profile_id, import_key)` where it is not null backs the pre-check. This follows `v1-roster-snapshot-import`, whose key also survives renames.

The deciding reason is the next slice. V1 has no team ids, and `warDefense2` names its slot teams by V1 name. War Defense ships later, so its import will usually be a separate run, after players may have renamed teams, and a V1 name over 40 characters never matches its shortened V2 name. Matching through the key resolves both.

Alternative considered: re-run by name only (the earlier choice). Simpler, but it re-imports renamed teams as duplicates and gives War Defense nothing stable to resolve against. The earlier worry that a key stops a deleted team from returning does not hold: the key is deleted with its row, so a re-run imports it again. `duplicate_name` remains for a V1 name that collides with a team the player made in V2.

### D9: Staging transfer stays at format version 1

`StagingTransferBundle` gains `Teams`, defaulting to empty, each carrying its `ImportKey` as roster snapshots do. The bundle is read leniently, so an older reader ignores it and no format bump is needed. The importer deletes the target profile's teams alongside its other planner data and adds the bundle's teams with new ids.

### D10: Rule codes are a contract enum

`issues` and a refused save's codes are typed as one `TeamRuleCode` enum in the OpenAPI document, not as free strings. A code added on the server then changes the generated `schema.d.ts`, and the apps' exhaustive mapping of codes to copy fails to type-check until the code has a translation. This turns the "client mirror drifts" risk into a build failure.

### D11: Migration

Migration `AddTeams` creates the table with a cascading foreign key to the profile, which also covers account purge. There is no backfill. The MoW flex index and the rarity cap were added after the migration was first generated; because it has never reached production, it is regenerated with them rather than followed by a second migration, and a local database that already applied it drops and recreates the `teams` table. Migrations apply on startup, and rollback is reverting the change, because no existing table changes.

### D12: MoW roles mirror characters

MoWs are split into core and flex the way characters are: `mow_ids` lists core MoWs first and `mow_flex_start_index` marks where flex begins. One shape for both keeps the record, the rules and the clients' lineup code alike, and a list of `{ id, role }` objects would let core and flex interleave for no benefit. Unlike characters, every MoW may be flex (index 0): a War team can plan "no MoW by default, bring one of these". `tooManyMows` becomes `tooManyCoreMows` (at most 1 core MoW in War and Tournament Arena); the rename is safe because the contract has not been released. V1 has no MoW roles, so its first MoW imports as core and the rest as flex, which keeps multi-MoW V1 teams valid instead of flagging them.

### D13: The rarity cap is stored, not enforced

`rarity_cap` is a nullable text column holding one of the six rarity names, validated on input. It records what the team is prepared for ("my Epic Guild War team") so clients can preview the lineup at that rarity. No rule reads it: the server does not know the player's roster, and a unit above the cap is simply played capped in the game.

## Risks / Trade-offs

- [The client mirrors the rules for instant feedback, so the two could drift] → The server stays authoritative, the codes are shared through the contract, and the companion change tests its mirror against the spec's scenarios.
- [A catalog update can flag a saved team, for example when an Incursion's factions change] → It stays readable and is reported as an issue. Nothing is deleted or repaired.
- [Imported teams with issues cannot be edited in small steps, because every save must pass] → Accepted as part of the strict decision; the client shows the issues in the editor.
- [`import-v1-roster-snapshots` has merged and already edits the V1 import request and response records] → This change merges second: add `teams` beside `rosterSnapshots` in the selection and response. The parts do not interact.
- [A War Defense slot will later reference a team that then loses its `warDefense` mode] → Out of scope here; the War Defense change decides whether to refuse the edit, clear the slot or flag it, and nothing in this change rules out any of the three. A slot can be a foreign key to `teams.id` with `ON DELETE SET NULL`, which is why D1 keeps one row per team.

## Migration Plan

Apply the migration on deploy, then ship the apps change. The apps change depends on these endpoints, so its PR merges after this one.
