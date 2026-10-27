# OpenSpec main-spec compaction audit — tacticus-planner-api

Scope: `tacticus-planner-api/openspec/specs/*/spec.md` (all 67 read in full). Line numbers are from the current files.

Rules I followed:
- OpenSpec needs at least one scenario per requirement. Where dropping scenarios would leave none, I give a replacement scenario.
- "Rewrite" entries give exact old text and new text. Everything else in the requirement block stays verbatim, because MODIFIED needs the full block.
- Purpose paragraphs can't be changed by a delta. They're listed separately at the end (P*) as direct edits.

Confidence: **H** = safe, **M** = verify first, **L** = optional or low value.

---

## 1. Requirements specified twice (D)

**D1 (M, optional, the biggest win). Catalog boilerplate has no canonical home.**
No spec states the rule for all datasets at once ("served anonymously, in the envelope, hashed in the manifest, raw file not served, ids only / no display text"), so every dataset spec repeats it. Every dataset route is in `GetGameCatalogDatasetEndpoints.cs` with `AllowAnonymous()`, so the rule holds catalog-wide. To compact, the change has to ADD it once.
- ADDED to `game-catalog-http-caching` (its Purpose already says "content-addressed and public"):
  ```
  ### Requirement: Every served dataset is public and listed in the manifest
  Every served game-catalog dataset SHALL be served at its own `GET /api/v1/game-catalog/{dataset}` endpoint without authentication, in the shared dataset envelope, and SHALL have a content-hash entry in the public manifest. Raw source files SHALL NOT be served directly. Served records SHALL carry ids and structured values only, with no display name, icon, icon id or wiki link; the client resolves presentation from ids.

  #### Scenario: A dataset is public and hashed
  - **WHEN** a client reads the manifest and then any dataset it lists, without credentials
  - **THEN** the dataset is returned in the shared envelope and its hash equals the manifest entry, and no endpoint serves a raw source file
  ```
- Then strip the copies:

| Where | Requirement (verbatim) | Action |
|---|---|---|
| character-ability-costs-dataset/spec.md:12-25 | `### Requirement: The catalog serves a \`character-ability-costs\` dataset` | MODIFIED: drop scenarios "Dataset is served without authentication", "Dataset appears in the manifest", "The raw source has no direct endpoint". This leaves no scenario, so add: `#### Scenario: One record per datamine row` / `- **GIVEN** the pinned \`abilityUpgradeCosts.json\`` / `- **WHEN** the catalog loads` / `- **THEN** \`character-ability-costs\` holds one record per row, in ascending \`level\``. Body: replace "at `GET /api/v1/game-catalog/character-ability-costs` without requiring authentication, consistent with the rest of the catalog" with nothing, and drop ", and the raw file SHALL NOT be served directly". |
| mythic-quests-dataset/spec.md:21-35 | `### Requirement: The catalog serves a \`mythic-quests\` dataset` | MODIFIED: drop the same three scenarios. Add: `#### Scenario: A misnamed mythic quest fails startup` / `- **GIVEN** a \`hero\` quest named \`mythic_blackAbaddon\`` / `- **WHEN** the catalog loads` / `- **THEN** loading fails naming \`mythic-quests\``. Body: drop "at `GET /api/v1/game-catalog/mythic-quests`, without requiring authentication, in the same dataset envelope as the other served datasets" and "The raw file SHALL NOT be served directly, and". |
| npcs-dataset/spec.md:9-19 | `### Requirement: The catalog serves one flat \`npcs\` dataset built from the per-faction raw sources` | MODIFIED: drop scenarios "Npcs dataset is served without authentication" and "Npcs dataset appears in the manifest". "Records from every raw source are present" remains. Body: drop "The raw file SHALL NOT be served directly. The dataset SHALL be served without authentication and SHALL appear in the catalog manifest with its own content hash." |
| raid-bosses-dataset/spec.md:11-19 | `### Requirement: The catalog serves one consolidated \`raid-bosses\` dataset` | MODIFIED: drop scenarios "Raid-bosses dataset is served without authentication" and "Raid-bosses dataset appears in the manifest". Keep "Raw sources have no direct endpoint", which names this dataset's specific files. |
| raid-bosses-dataset/spec.md:26-33 | `### Requirement: Raid-boss records carry no display text or icon` | REMOVED. Covered by the new catalog rule. |
| game-shops-dataset/spec.md:21-34 | `### Requirement: The catalog serves one consolidated \`shops\` dataset` | MODIFIED: drop scenarios "Shops dataset is served without authentication", "Shops dataset appears in the manifest", "Raw per-shop sources have no direct endpoint". Keep "All four daily shops are present". Body: drop "; the raw merchants file SHALL NOT be served directly". |
| game-shops-dataset/spec.md:41-51 | `### Requirement: Shop records carry no display text or icon` | REMOVED. Covered by the new catalog rule. |
| game-events-calendar-dataset/spec.md:107-113 | `### Requirement: event-definitions and events-calendar are served; event-occurrences is not` | MODIFIED: drop scenarios "Served datasets appear in the manifest" and "Served dataset is served without authentication". Keep "event-occurrences has no direct endpoint". |
| unit-power-dataset/spec.md:43,78-81 | `### Requirement: The catalog serves one unit-power record` | MODIFIED: drop sentence "The dataset SHALL be listed in the public manifest with its content hash." and scenario "Listed in the manifest". |
| horde-mode-tiers-dataset/spec.md:21,47-50 | `### Requirement: The catalog serves one horde-mode tier list` | MODIFIED: drop sentence "The dataset SHALL be listed in the public manifest with its content hash." and scenario "Listed in the manifest". |

Leave as is: `game-events-calendar` "Event definitions and occurrences carry no display text or icon" (it also constrains raw-only authored occurrences, so it differs). Leave the `guild-raid-meta` and `quest-events` inline mentions (one clause each, inside shape requirements).

**D2 (H).** character-ability-costs-dataset/spec.md:47-54, `### Requirement: Records carry no display text or icon`. This repeats the same spec's `### Requirement: Each record is keyed by the level it raises an ability to` (line 29: "SHALL carry exactly these fields and no others"). Its scenario "Served payload shape" ("only `level`, `gold` and `badges`") repeats that too. → REMOVED. Applies with or without D1.

**D3 (H).** game-shops-dataset/spec.md:196-204, `### Requirement: Shop events are out of scope`. Req 1 already says the dataset has one record per daily shop, and its scenario "All four daily shops are present" asserts *exactly* `guild`, `war`, `rogue-trader`, `crusade`, which rules out event shops. → REMOVED.

**D4 (H). `hseVariant` validation is specified twice.**
- Canonical: game-events-calendar-dataset/spec.md:43-61, `### Requirement: Required parameters are enforced per definition`. It has positive and negative scenarios, and the negative one is `hse-defeat-waves` / `run-3`.
- Duplicate: home-screen-event-scoring/spec.md:95,102-105, `### Requirement: Scoring data is derived from the V1 datamine and validated`. → MODIFIED:
  - Drop the clause ", or an occurrence's `hseVariant` is not a variant of its definition". The sentence then ends "...a `UnitXp` modifier lacks exactly one of `multiplier` and `multiplierByRarity`."
  - Drop scenario "Unknown variant on an occurrence fails the build". It's the same `hse-defeat-waves`/`run-3` case.

**D5 (H). Curated LRE file holds only run starts.**
- Canonical: legendary-event-datamine-source/spec.md:17-24, `### Requirement: Only run starts are curated`.
- Duplicate: legendary-event-run-schedule/spec.md:26, `### Requirement: The served array is derived from the raw run starts`. → MODIFIED: drop the sentence "The raw files SHALL NOT carry `eventStage`, `nextEventDate`, `nextEventDateUtc` or `finished`, since each is derivable from `runStartDatesUtc`." (See L27 for the stale raw-file names in the same spec.)

**D6 (L, optional, small gain). `killPoints` = first `defeatAll` is stated twice.**
- Canonical: legendary-event-lane-points/spec.md:8-10, `### Requirement: A lane's killPoints is its defeat-all base`.
- Duplicate: legendary-event-datamine-source/spec.md:55-57, `### Requirement: Lane points come from the Acing objective`. → MODIFIED: replace "its score is that battle's `defeatAll` value and the first battle's is the track's `killPoints`." with "its score is that battle's `defeatAll` value (the track's `killPoints` follows `legendary-event-lane-points`)."

**D7 (H). "GET never calls upstream" is stated four times in guild-raid-current-status.**
- Canonical: `### Requirement: Reading status never calls upstream; forced refresh is a separate cooldown-gated endpoint` (line 152).
- MODIFIED `### Requirement: Current Guild Raid status is scoped to a ready registered guild` (line 13). Replace "It SHALL preserve the existing not-found behavior for an unprovisioned profile. `GET` SHALL NOT call the upstream Guild Raid API under any circumstance, including these prerequisite-failure cases and the never-observed case." with "An unprovisioned profile SHALL receive a not-found response." This also fixes the "existing" delta wording.
- MODIFIED `### Requirement: Refresh failures distinguish transient unavailability from rejected source data` (line 180). Drop "These failure responses SHALL only originate from `POST /refresh`; `GET` never produces them because it never calls upstream." It also contradicts the last requirement, under which GET does return bad gateway.
- MODIFIED `### Requirement: A retained observation that cannot be projected is a bad-gateway response` (line 203). Replace "`GET /api/v1/guild/raid-status` never calls upstream, but it still projects the retained observation" with "`GET /api/v1/guild/raid-status` projects the retained observation".

**D8 (H). Stale goal revision is restated inside goal-combined-edit.**
- Canonical: goal-target-editing `### Requirement: Concurrent edits and target history are explicit`, plus goal-combined-edit's own `### Requirement: Conflicts leave the goal unchanged and carry the current state`.
- Duplicate: goal-combined-edit/spec.md:35, `### Requirement: Each section is validated exactly as its own endpoint validates it`. → MODIFIED: replace "`expectedRevision` is required with a target and a stale one is a 409 `goalRevisionStale` carrying the current goal)" with "`expectedRevision` is required with a target)".

**D9 (M, differs, needs a check). Acquisition sources on update.**
- goal-target-model/spec.md:217-232, `### Requirement: Acquisition sources can be edited on an existing goal`, says update is allowed for "an Unlock or Ascension goal" only.
- But `### Requirement: Source kinds are gated by entity and goal type` (line 118: "SHALL apply identically on goal creation, combined goal creation, and goal update") and its scenario "Mythic material shop source on a Character Upgrade goal is accepted" (an *update*, line 146) allow it for Rank/Upgrade/Ability too. goal-combined-edit Req 2 also covers the `details.acquisitionSources` validation.
- → MODIFIED: replace "`UpdateGoalRequest` SHALL allow replacing an Unlock or Ascension goal's `acquisitionSources`," with "`UpdateGoalRequest` SHALL allow replacing a goal's `acquisitionSources`,".
- Drop scenario "Invalid update is rejected without changing the goal". It's covered by "Unknown kind is rejected" plus the combined-edit atomic 400.
- Keep the unique rule "Clearing the field SHALL reset the goal to unrestricted campaign farming."
- Confirm in code that Rank/Upgrade/Ability updates are allowed before rewriting.

**D10 (M, an inconsistent duplicate, needs a decision).** The effective-progression cap rule appears in two places:
- goal-target-model/spec.md:274-293, `### Requirement: Progression-derived target caps resolve against the request's effective progression`, and :322-327, `### Requirement: The raised cap requires a declared dependency`. These say only in-request Ascension specs raise the cap.
- goal-dependencies/spec.md:24-26, `### Requirement: An existing Ascension dependency lifts the cap floor`. This is the superset: it adds existing goals reachable from `dependsOnGoalIds`.
- So goal-target-model is stale and now contradicts goal-dependencies. Smallest fix, keeping both:
  - MODIFIED first requirement: add a third bullet after line 285: "- the end of every existing Ascension goal reachable from the goal's `dependsOnGoalIds` (see `goal-dependencies`)."
  - MODIFIED second requirement: replace "only from Ascension specs it declares a dependency on within the same request." with "only from Ascension specs it declares a dependency on within the same request, or existing Ascension goals reachable from its `dependsOnGoalIds` (see `goal-dependencies`)."
  - Also see L14 (line 320).

**D11 (H). rank-milestones restates goal-target-milestones and project-goal-slots.**
- MODIFIED rank-milestones/spec.md:9-23, `### Requirement: Rank target identity is the normalized end state`.
  - New body: "For duplicate detection, a Rank target SHALL be identified by end rank, end point-five state, and end applied-upgrade count after normalization."
  - This drops "start/baseline and farming strategy SHALL not make the same end target distinct", which is goal-target-milestones Req 1. It also drops "Distinct targets SHALL retain distinct goal ids, statuses, events, and project memberships", which is goal-target-milestones `### Requirement: Distinct-target goals are independently managed`; "events" is also gone (goal-collection-read: no history).
  - Drop scenario "Different targets are distinct" (= project-goal-slots "Same project permits different Rank targets"). Keep "Equivalent end target conflicts".
- REMOVED rank-milestones/spec.md:25-38, `### Requirement: Deletion frees the active target without restoring stale state`.
  - Covered by goal-target-milestones "Distinct-target goals are independently managed" ("Deleting or completing one SHALL free only its own target") and project-goal-slots Req 1 ("Completed/Archived goals SHALL not occupy slots" + scenario "Historical goal frees slot" ≈ "Historical milestone does not block").
  - Its remaining content (a recreated goal gets a new id and no old memberships or "events") is trivially true.
- After this, rank-milestones holds only the Rank normalization. goal-target-milestones:36,46 and project-goal-slots:9 reference it. Folding it entirely into goal-target-milestones Req 2 is possible but a bigger diff; not recommended.

**D12 (H).** REMOVED project-goal-slots/spec.md:123-131, `### Requirement: Rank conflicts name the occupied target`.
- The 409 payload (existing goal id, entity, goal type, `normalizedTarget`) is fully in `### Requirement: Conflicts identify affected projects and goals`.
- Atomic failure of target edits and transitions is in `### Requirement: Every membership and lifecycle path enforces slots`.
- Its scenario (edit Silver3→Gold1 that is already held) = goal-target-editing "Rank target conflicts in one project".

**D13 (H). "Membership is not slot-checked" is stated twice.**
- Canonical: project-goal-slots `### Requirement: Every membership and lifecycle path enforces slots` (line 55: "Membership changes (goal-side or project-side) SHALL NOT be slot-checked, because membership cannot create a duplicate"), with scenario "Shared goal occupies each project once".
- Duplicate: project-bulk-membership-replacement/spec.md:20,26-29, `### Requirement: Batch validation is atomic and actionable`. → MODIFIED:
  - Drop "Membership replacement SHALL NOT perform slot checks: in-flight uniqueness is a per-profile property of the goal, so adding an existing goal to a project can never create a duplicate."
  - Drop scenario "Occupied slot". "One blocked removal among valid additions" remains.

**D14 (M).** REMOVED goal-bulk-status/spec.md:50-57, `### Requirement: Bulk transitions serialize with other goal mutations`.
- Covered by project-goal-slots `### Requirement: Concurrent mutations in one project serialize without failing`: a profile lock; covered mutations include "status transitions"; an order change racing an in-flight-set change is rejected stale; priorities stay contiguous.
- Its scenario "Reorder races a bulk complete" ≈ "Ordering stays contiguous under concurrency".
- Keep goal-combined-edit's lock requirement. It adds re-read-under-lock and no-deadlock specifics.

**D15 (H). Capability-level redundancy: `project-unit-ordering` is fully covered elsewhere.** REMOVED all four requirements, then delete `openspec/specs/project-unit-ordering/` (no spec references it):
- `### Requirement: Flattened order remains the canonical scheduler order` (:7-17)
  - Covered by global-goal-priority `### Requirement: Account has one canonical in-flight order` ("listing a project SHALL preserve the same relative positions"; scenario "Mixed unit types … without unit regrouping") and goal-collection-read `### Requirement: The projects list carries goal ids`.
  - It also constrains "project-goal list responses", but goal-collection-read says `GET /me/projects/{projectId}/goals` SHALL NOT exist. Its Completed ordering "goal-creation-time" is a stale variant of goal-collection-read's "newest first", not a live alternative.
- `### Requirement: Goal creation does not accept numeric priority` (:19-29)
  - Superseded by global-goal-priority `### Requirement: Lifecycle changes maintain global order`: new goals enter once each and append, *and* creation now accepts `position`. That makes "does not accept caller-authored numeric priority" stale.
- `### Requirement: Project goal order is addressable through a dedicated operation` (:31-50)
  - Covered by global-goal-priority `### Requirement: Owner can reorder the complete global set` and `### Requirement: Owner can move a goal within a project's projection`.
  - Scenario "Reorder within a project writes through to global order" is word-for-word "Moving a member up within the project". "Stale goal set is rejected atomically" ≈ "Concurrent addition". "A goal may be positioned ahead of an unreached prerequisite" ≈ "Dependency appears after its dependent".
- `### Requirement: Membership replacement does not accept caller-authored priority` (:52-62)
  - Covered by project-bulk-membership-replacement `### Requirement: Membership save does not reorder global goals` and global-goal-priority Req 4 ("Membership edits … SHALL not change global positions").
  - Scenario "A newly added member appends" (whose body says it *doesn't* append) ≈ "Added goal keeps its global position". The "legacy priority value" scenario is a leftover.
- Check whether `openspec archive` accepts a delta that removes every requirement of a capability. If not, delete the folder in the same PR.

**D16 (H).** default-project/spec.md:30, `### Requirement: The Default project cannot be archived or removed`. → MODIFIED: drop "No other project SHALL be blocked from archiving because of any selection or plan standing, since the system SHALL NOT record any project as the profile's active plan." It repeats `### Requirement: No active-project selection is exposed` and is transition wording. Scenario "Any custom project can be archived" stays.

---

## 2. Scenarios pasted under several requirements (S)

| ID | Duplicate (drop) | Kept copy | Action |
|---|---|---|---|
| S1 (H) | project-unit-ordering:39-42 "Reorder within a project writes through to global order" | global-goal-priority:41-44 "Moving a member up within the project" (identical) | Handled by D15 |
| S2 (H) | goal-combined-edit:42-46 "Project slot check sees the new target" | goal-target-editing:39-43 "Rank target conflicts in one project" | MODIFIED `### Requirement: Each section is validated exactly as its own endpoint validates it`: drop the scenario |
| S3 (H) | project-goal-slots:57-61 "Resume conflicts in one membership" | goal-bulk-status:33-37 "One conflicting resume" (same case, plus the multi-goal atomicity) | MODIFIED `### Requirement: Every membership and lifecycle path enforces slots`: drop the scenario. "Shared goal occupies each project once" remains |
| S4 (H) | project-goal-slots:17-21 "Same project rejects duplicate" | Same requirement, :11-15 "Different projects accept different instances" (same rejection, plus the add-to-B path) | MODIFIED `### Requirement: In-flight uniqueness is scoped to project`: drop the scenario |
| S5 (H) | project-goal-slots:89-92 "Racing creates target one slot" | :113-116 "Racing same-slot creates produce a conflict, not a server error" (stricter) | MODIFIED `### Requirement: The profile lock serializes slot checks`: drop the scenario. "Racing distinct Rank targets succeed" remains (same type, distinct targets; not covered elsewhere) |
| S6 (H) | guild-raid-meta:98-103 "A boss has Meta and alternate exact teams" | :105-111 "A boss has several tiered recommendations" (same THEN, plus authored order) | MODIFIED `### Requirement: Boss Meta recommendations provide exact teams`: drop the scenario |
| S7 (H) | v1-goal-import:360-364 "A created prerequisite precedes its dependents in the import sequence" | :256-260 "A created prerequisite is placed ahead of its own V1-relative position" | MODIFIED `### Requirement: Missing prerequisites are created automatically by the same rules as manual creation`: drop the scenario |
| S8 (H) | v1-goal-import:366-370 "Rank and Ability imports do not synthesize Level" | Same requirement :353-358 "A requirement already met needs no prerequisite" (below level → nothing created), plus rank-level-progression Req 2 (Level doesn't exist) | Same MODIFIED as S7: drop the scenario (a transition leftover; the Level type is gone) |
| S9 (L) | legendary-event-plans:204-208 "Stale revision on a run write" | Same requirement :198-202 "Stale revision" (the requirement already says "every mutation") | MODIFIED `### Requirement: One revision per plan guards every mutation`: drop the scenario |
| S10 (H) | rank-milestones "Different targets are distinct", "Historical milestone does not block" | project-goal-slots "Same project permits different Rank targets", "Historical goal frees slot" | Handled by D11 |

**Checked, not duplicates (leave them):**
- "Unknown event": plans (404 for read/write), run-records (404 only when it has no records *and* is not in the catalog), project-event-target (400 `target.eventId`). Different endpoints and rules.
- "Run outside the event": plans team `run` field vs `/runs/{run}` route. Different requirements and endpoints.
- "Stale revision" / "Stale order revision": global-goal-priority project move, goal-bulk-status, goal-combined-edit (cross-section atomicity), legendary-event-plans, user-settings "Stale revision still conflicts". Each guards a different operation. The api-error-contract examples are the cross-cutting shape and stay.
- "Coverage": mythic-quests vs quest-events, different datasets. See L10 for the leftover inside quest-events.
- "An unknown cost key": ascension-costs vs mow-upgrade-costs, different datasets and keys.
- "Served payload shape": character-ability-costs (D2) and raid-bosses (D1).
- "Listed in the manifest", "Dataset appears in the manifest", "Dataset is served without authentication", "The raw source has no direct endpoint": D1.
- "Same ladder as before", "Existing datasets and schema version unchanged": leftovers (L9, L5, L6).
- "A boss has no curated recommendation" vs "A prime has no curated recommendation": different collections.
- goal-lifecycle-status "A paused new goal still occupies its project slot": specific to `startPaused`, keep.
- goal-target-milestones "Unlock remains single in a request" vs project-goal-slots "Unlock stays one per unit": in-request vs existing, keep.

---

## 3. Leftovers from archive (L)

**Whole requirements that only describe a migration or transition → REMOVED**

| ID | Where | Requirement (verbatim) | Reason |
|---|---|---|---|
| L1 (H) | game-catalog-datamine-source:101-108 | `### Requirement: Adding the datamine source changes no served data` | "Applying this change SHALL NOT…" / "after this change". One-off. |
| L3 (H) | campaign-battles-dataset:33-40 | `### Requirement: Adding allies does not break the served contract` | "The change SHALL NOT bump…"; scenario "Manifest after the change". |
| L4 (H) | npcs-dataset:100-112 | `### Requirement: Adding the classification and faction fields does not change the dataset's identity or schema version` | "The change SHALL…", "after this change", "pre-change hash". |
| L5 (H) | raid-bosses-dataset:166-178 | `### Requirement: Adding the raid-bosses dataset does not bump the schema version` | "Introducing … purely additive"; scenario "Existing datasets and schema version unchanged". Caveat: "GameVersion SHALL record the in-game version" has no other home. If you want to keep that, it belongs to catalog metadata (`game-catalog-datamine-source` REF), not here. The scenario also says "from the V1 datamine", which is stale. |
| L6 (H) | game-shops-dataset:206-214 | `### Requirement: Adding the shops dataset does not bump the schema version` | Same pattern. |
| L11 (M) | legendary-event-reward-ladder:45-52 | `### Requirement: No shared reward ladder dataset is served` | Describes the removal of `lre-common` ("The manifest snapshot SHALL reflect the removal…", scenario "Manifest no longer lists lre-common"). If you'd rather keep a guard: MODIFIED instead, dropping "The manifest snapshot SHALL reflect the removal and the `lres` dataset's new content hash." |
| L12 (H) | goal-target-model:30-38 | `### Requirement: Persisted independent equipment goals are removed` | "The migration SHALL delete existing goals…". |
| L13a (M) | goal-target-model:188-203 | `### Requirement: The single-select ascension farming source model is removed` | Transition ("OpenAPI no longer describes the old model", "legacy `ascensionFarming`"). Its one live rule moves into L13b. |
| L15 (H) | rank-level-progression:38-52 | `### Requirement: Existing Level goals are removed by migration` | "Applying the migration…", "the migration runs". |
| L17a (H) | player-data-sync:38-43 | `### Requirement: Existing stale snapshots recover through normal synchronization` | Fix-transition ("previously stale", "without requiring a database migration", scenario "Corrected server…"). The behavior is implied by Req 1 and Req 2 of the same spec. |

**Scenarios to drop inside a MODIFIED requirement**

| ID | Where | Requirement | Action |
|---|---|---|---|
| L2 (H) | game-catalog-datamine-source:49-52 | `### Requirement: Only allowlisted files are copied, unchanged` | Drop scenario "The seeded allowlist" ("WHEN `add-datamine-source` was applied"). Two other scenarios remain. |
| L7 (H) | mows-dataset:12,30-33 | `### Requirement: Each MoW carries its mythic ability` | Drop scenario "The manifest hash changes" ("after the mythic ability was added … release before it"). Also replace "SHALL carry, in addition to its existing fields, `mythicAbility`:" with "SHALL carry `mythicAbility`:". |
| L20b (H) | default-project:72-75 | `### Requirement: No active-project selection is exposed` | Drop scenario "The activate operation is gone" ("the former activate operation"). "Project summaries carry no active-plan flag" remains. |

**Scenarios that compare to retired files ("same as before") → replace (the requirement would otherwise have no scenario)**

| ID | Where | Requirement | Replacement scenario |
|---|---|---|---|
| L9a (H) | ascension-costs-dataset:12-15 | `### Requirement: The ascension ladder is built from the pinned datamine file` | `#### Scenario: The shipped ladder` / `- **WHEN** the dataset is served at datamine \`v1.43.157.2989\`` / `- **THEN** it holds 20 records, the first \`Common:None\`` |
| L9b (H) | characters-dataset:12-15 | `### Requirement: Characters are read from the pinned datamine` | `#### Scenario: The shipped characters` / same WHEN / `- **THEN** it serves 119 characters` |
| L9c (H) | equipment-dataset:14-17 | `### Requirement: Equipment is built from the pinned datamine \`items.json\`` | `#### Scenario: The shipped items` / `- **WHEN** the \`equipment\` dataset is served at datamine \`v1.43.157.2989\`` / `- **THEN** it holds 217 records` |
| L9d (H) | mow-upgrade-costs-dataset:14-17 | `### Requirement: The MoW cost ladder is built from the pinned datamine file` | `#### Scenario: The shipped ladder` / same WHEN / `- **THEN** it holds 59 records` |
| L9e (H) | mows-dataset:50-53 | `### Requirement: MoWs are read from the pinned datamine` | `#### Scenario: The shipped MoWs` / same WHEN / `- **THEN** it serves 11 MoWs` |
| L9f (H) | upgrades-dataset:12-15, :40 | `### Requirement: Upgrade materials are built from the pinned datamine files` | `#### Scenario: The shipped materials` / same WHEN / `- **THEN** it holds 560 records, including \`upgArmL008\` (Legendary, Armour, not craftable)`. Also MODIFIED `### Requirement: The served order is stable`: drop ", matching the order the retired files gave". |
| L9g (H) | raid-bosses-dataset:184-187 | `### Requirement: Raid-boss unit sets are merged across the game's sets` | Keep the title "Identical sets merge" and the WHEN. New THEN: `- **THEN** they merge into one map of 98 unit sets and loading succeeds` |
| L8 (H) | equipment-dataset:55-58 | `### Requirement: Equipment is served in type, then id order` | Replace "The migration moves only the order" (mentions `migrate-equipment-to-datamine`) with `#### Scenario: Type, then id` / `- **GIVEN** two items of one \`type\` with ids \`x2\` and \`x1\`, and an item whose \`type\` sorts before theirs` / `- **WHEN** the \`equipment\` dataset is served` / `- **THEN** the order is the earlier-typed item, \`x1\`, \`x2\`` |

**Sentence and phrase rewrites (MODIFIED)**

| ID | Where | Requirement | Old → New |
|---|---|---|---|
| L10 (H) | quest-events-dataset:63 | `### Requirement: The catalog serves a \`quest-events\` dataset with a fixed shape` | "each with 17 battles; the three V1 quests are unchanged" → "each with 17 battles" |
| L13b (M) | goal-target-model:205-215 | `### Requirement: FarmingLocationIds retains its upgrade-node role` | Body → "`GoalConfig.farmingLocationIds` SHALL be accepted and returned as the Rank/Ability upgrade-node farming override. Unlock and Ascension shard sources SHALL be expressed only through `acquisitionSources`." Scenario title "Rank goal farming override is unchanged" → "Rank goal farming override round-trips". |
| L14a (H) | goal-target-model:246-247 | `### Requirement: Upgrade goal targets accept decomposed base materials` | Delete "Widening relevance SHALL NOT invalidate any previously accepted target." At :266, "THEN the request is accepted, as before this change" → "THEN the request is accepted". |
| L14b (H) | goal-target-model:320 | `### Requirement: Progression-derived target caps resolve against the request's effective progression` | "THEN creation succeeds, unchanged from prior behavior" → "THEN creation succeeds". Do this in the same MODIFIED as D10. |
| L14c (H) | goal-target-model:349 | `### Requirement: Unlock eligibility counts shop shard offers` | "A character with neither source SHALL be rejected as before." → "A character with neither source SHALL be rejected." |
| L14d (H) | goal-target-model:409-413 | `### Requirement: Upgrade goals carry an optional progression range` | Scenario "Existing and range-less goals are unchanged" → title "Range-less goal"; GIVEN "an Upgrade goal created before this change, or created without any range group" → "an Upgrade goal created without any range group". |
| L16 (H) | openapi-contract:70-74 | `### Requirement: Enum wire values are stable` | "Enum fields SHALL serialize to the same PascalCase strings the API returned before this change." → "Enum fields SHALL serialize to PascalCase strings (e.g. `Active`, `Rank`, `Character`)." Scenario "Response values are unchanged" → title "Response values are PascalCase"; THEN → "`status`, `goalType` and `entityType` carry PascalCase strings (e.g. `Active`, `Rank`, `Character`)". |
| L17b (L) | player-data-sync:53-56 | `### Requirement: Simultaneous farm rewards have one combined expected yield` | Scenario "A reward occurs only once in a battle", THEN "its existing guaranteed and drop-chance semantics remain unchanged" → "its location is that single occurrence, with its own guaranteed flag and, when probabilistic, its own chance ID, numerator and denominator". |
| L18 (H) | onslaught-progress-overrides:34 | `### Requirement: Saving can leave an alliance unset` | Delete "Revision and conflict handling SHALL be unchanged." (a delta remark; no spec owns those rules). |
| L19 (H) | global-goal-priority:65 | `### Requirement: Lifecycle changes maintain global order` | "Membership edits and changing the Current plan SHALL not change global positions." → "Membership edits SHALL not change global positions." (There is no "Current plan"; see default-project.) |
| L20a (M) | default-project:10, :20 | `### Requirement: Every profile has exactly one Default project` | "created by migration or on first need" → "created on first need". Scenario "Existing profile without a Default project", WHEN "the migration has run or the profile's projects are first needed" → "the profile's projects are first needed". |
| L21 (H) | project-goal-slots:87 | `### Requirement: The profile lock serializes slot checks` | "so two concurrent transactions cannot both occupy the same non-Rank profile/unit/type slot or the same Rank profile/unit/normalized-end-target slot." → "so two concurrent transactions cannot both occupy the same slot." (Left over from when only Rank had a target key.) Do this in the same MODIFIED as S5. |
| L21b (M) | project-goal-slots:7 | `### Requirement: In-flight uniqueness is scoped to project` | RENAMED → `### Requirement: In-flight uniqueness is scoped to the profile` (the body says per profile, membership-independent). Optionally also RENAMED `### Requirement: Concurrent mutations in one project serialize without failing` → `### Requirement: Concurrent goal mutations of one profile serialize without failing`. In the delta, RENAMED applies before MODIFIED (S4 uses the new name). |
| L22 (M) | legendary-event-plans:51, :101 | `### Requirement: Teams belong to one lane of the plan` | "A team stored with fewer than three members (written before this rule) SHALL stay readable" → "A stored team with fewer than three members SHALL stay readable". Scenario "A short team written earlier stays readable", GIVEN "a stored alpha team with members `[u1, u2]` written before the minimum existed" → "a stored alpha team with members `[u1, u2]`". |
| L23 (H) | v1-goal-import:294-296 | `### Requirement: Missing prerequisites are created automatically by the same rules as manual creation` | Delete "There is no automatic within-unit reordering to fall back on for this placement — the import process itself is responsible for the prerequisite's position, not a downstream ordering step." (It refers to the retired unit-block ordering.) Do this in the same MODIFIED as S7 and S8. |
| L24 (H) | guild-raid-meta-dataset:187-190 | `### Requirement: Exact recommendations expose explicit variant rules` | "the recommendation-level `efficiency` figure defined above is the one deliberate exception to this dataset's prior no-effectiveness-weight constraint." → "the recommendation-level `efficiency` figure defined above is the only effectiveness value served." |
| L25 (L) | goal-target-milestones:43; legendary-event-lane-points:17 | `### Requirement: Target keys are defined per goal type`; `### Requirement: A lane's killPoints is its defeat-all base` | Scenario titles only: "Rank key is unchanged" → "Rank key"; "Other events are unchanged" → "Other events". Cosmetic. |

**Stale references to retired raw files (L26–L27, M, optional).** These are not delta wording, but the text describes the pre-datamine layout.

| ID | Where | Requirement | Old → New |
|---|---|---|---|
| L26 | npcs-dataset:7 | `### Requirement: The catalog serves one flat \`npcs\` dataset built from the per-faction raw sources` | RENAMED → `### Requirement: The catalog serves one flat \`npcs\` dataset` |
| L26 | npcs-dataset:74, 78, 85-88, 92 | `### Requirement: Each NPC record is classified as a unit, Machine of War, or loot object` | "loaded from `npcs-objects.json`" → "in the `npcs-objects` source group" (code: `NpcDenormalizer.NpcObjectsSourceKey`). "is loaded from `npcs-necrons.json`" → "is in the `npcs-necrons` source group". |
| L27 | legendary-event-run-schedule:14, 20, 26, 45 | all four requirements | Run starts now live in `Data/curated/lre-curation.json` (per event `runStartDatesUtc`). The spec still says "raw `lres-votanuthar.json`", "raw file", "raw `lres-*.json` file". Rewrite "the raw `lres-<x>.json` has" → "the curated `lre-curation.json` entry for `<id>` has", and "raw file"/"raw `lres-*.json` file" → "curated entry". Same issue in legendary-event-lane-points:24 and legendary-event-missions-dataset:24 ("a raw `lres-*.json`"). Lower priority: the validators still name a source file. |

**Purpose paragraphs (not expressible as delta; edit directly in the same PR) (P)**
- P1 game-catalog-datamine-source:5: drop "Datasets move onto it one change at a time."
- P2 goal-target-model:3: "Defines the supported unit-centered goal target model after removal of independent equipment goals." → "Defines the supported unit-centered goal target model."
- P3 goal-bulk-status:4: drop ", replacing the per-goal and per-project status routes".
- P4 project-goal-slots:3: "preventing duplicate Active/Paused goal types for one unit inside a project" → "preventing two in-flight goals of one unit, type and target key in a profile".
- P5 mows-dataset:5-6: "starting with each Machine of War's mythic ability, so the MoW unit page can show…" is delta-flavoured. Optional.
- P6 home-screen-event-scoring:3 "imported from the V1 datamine" (and the header `### Requirement: Scoring data is derived from the V1 datamine and validated`): the source is the pinned datamine. Optional RENAMED.
- P7 legendary-event-reward-ladder:4 "and that no shared cross-event ladder dataset is served": drop if L11 is applied.
- P8 project-unit-ordering: deleted with D15.

**Looked at and deliberately not flagged.** These are steady-state, or the comparison is documentation:
- V1-comparison notes in scenarios: character-ability-costs "Game values replace V1's"; onslaught-rewards "Bronze-1 takes the game's badges"; unlock-shard "(V1's 1400 is dropped)"; mythic-quests Coverage "(adds … to V1's 117)"; LE missions "Uthar's premium missions follow the game"; upgrades "Names are the game's untranslated names".
- user-settings "Legacy stored settings read the default" (real stored-data case).
- goal-lifecycle-status `### Requirement: Creation status does not depend on project membership`.
- goal-target-model `### Requirement: UpgradeItem goal type is removed` and rank-level-progression `### Requirement: The Level goal type does not exist`. Both still specify a live rejection.
- "archived" goals and projects (a domain concept).

---

## Substance inconsistencies found while auditing (not compaction; for a separate fix)
- user-settings:25 says `xpBookRarity` "Values are case-sensitive exactly as listed", but openapi-contract:70 says enum requests are accepted case-insensitively. One of them is wrong if `xpBookRarity` is an enum.
- v1-goal-import:124 skips when "the account already has a goal of that type for that unit", but slots are now per target key (goal-target-milestones, project-goal-slots).
- global-goal-priority uses `GET /goals`, `PUT /goals/order`, `PUT /projects/{projectId}/goal-order`, while goal-collection-read and goal-combined-edit use `/me/goals…`.
- player-data-sync hosts two catalog requirements: `### Requirement: Simultaneous farm rewards have one combined expected yield` and `### Requirement: Served campaign battles carry their Tacticus battle index`. They belong with the campaign-battles specs; moving them is a separate decision.
