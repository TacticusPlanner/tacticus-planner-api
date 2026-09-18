## Why

V1 goal import is unreliable and lossy. Users report a minority of their goals
arriving — "2 created, 2 skipped, 17 failed", then one or two more per retry —
with counts that do not match what actually landed, an order unrelated to their
V1 plan, and goals whose farming source silently changed meaning.

The cause is the split design. The endpoint translates V1 goals into
create-request specs and hands them back for the client to submit, one request
per unit, in parallel. That produced four independent defects:

- **Most requests fail.** Concurrent goal creation in one project could not
  both commit (fixed separately by `fix-goal-mutation-isolation-level`).
- **The reported counts are incoherent.** "Created" and "failed" count unit
  specs, while "skipped" counts source V1 goals, so they are in two different
  units and sum to nothing meaningful. A user who imported 5 goals across 4
  units is told "4 created".
- **V1 ordering is discarded.** The translator knows the V1 priority order and
  returns specs in it; parallel submission then makes final order the race
  outcome.
- **Data is dropped silently.** V1's shard-source fields are not parsed at all,
  so an Onslaught-sourced Ascension goal imports as unrestricted campaign
  farming — the opposite constraint. V1 goal notes are computed and then
  discarded because the wire spec has no field for them. Duplicate Rank and
  Ascension goals are merged with no record. And with no player data synced,
  every "target already reached" check silently passes, so goals import with
  fabricated starting points and spurious Unlock goals for owned characters.

The justification for the split — that the server cannot build a goal's
initial-state snapshot — does not hold: every value that snapshot needs is
already read from the player data snapshot this same service queries, and the
one remaining input is passed as empty by the import path anyway.

## What Changes

- **Goal creation moves into `POST me/v1-import`.** The endpoint creates the
  goals itself, in V1 priority order, and no longer returns create-request
  specs for the caller to submit. **BREAKING**: `goalSpecs`, `goalsSkipped`,
  and `goalIssues` are removed from the response.
- **One outcome per source V1 goal.** The response carries exactly one entry
  per goal in the V1 profile, each with a status of created, skipped, or
  failed, a stable code, a message, the unit and goal type it concerned, and
  the id of the V2 goal it produced or matched. Counts become derivable and
  unambiguous.
- **Skips are classified.** A goal skipped because its target is already
  reached, already exists, or was merged into another is reported distinctly
  from one not imported because its unit is unknown to the catalog, its V1
  goal type is unsupported, or its target could not be translated.
- **The goals part is refused without player data.** If the account has no
  player data snapshot, the goals part reports a single blocking outcome
  telling the user to sync first, and no goals are created. It no longer
  imports goals with invented starting points.
- **V1 shard sources are honoured.** V1's shard farm type and campaign-usage
  fields are parsed and translated into the goal's acquisition sources, so an
  Onslaught ascension stays an Onslaught ascension.
- **V1 goal notes are carried across.**
- **Missing prerequisites are created automatically**, by the same rules the
  manual create-goal flow applies: an Unlock goal for a unit not in the roster,
  an Ascension goal when a target is above the unit's progression-derived cap,
  and a Level goal when a target is above the unit's level — each at the
  minimum target that satisfies the requirement, wired as a dependency of the
  goal that needed it. Controlled by a new selection flag, defaulting on.
- **The snapshot is built server-side** from the player data the translator
  already loads.
- **V1 goal types 6 and 7** (upgrade-material and pre-farm-material goals)
  report as not supported, with no roadmap promise.

## Capabilities

### New Capabilities

- `v1-goal-import`: importing a V1 planner profile's goals — which V1 goals
  translate to which V2 goals, how targets and shard sources are derived, when
  a goal is skipped versus not imported, the ordering guarantee, automatic
  prerequisite creation, and the per-goal outcome report.

### Modified Capabilities

<!-- none. Acquisition-source shape and validation are already specified by
     `goal-target-model`; this change consumes them rather than changing them.
     Ordering relies on `project-unit-ordering`'s existing "goal creation does
     not accept numeric priority" requirement rather than altering it. -->

## Impact

- `src/TacticusPlanner.Api/Features/V1Import/V1GoalImportService.cs` — becomes
  a translate-and-create service: builds snapshots, resolves acquisition
  sources, synthesizes prerequisites, and emits per-source-goal outcomes.
- `src/TacticusPlanner.Api/Features/V1Import/ImportV1ProfileEndpoint.cs` —
  response shape; the goals part's blocking behavior; the new selection flag.
- `src/TacticusPlanner.Api/Features/V1Import/TacticusV1Client.cs` — the V1 goal
  DTO gains the shard-source fields it currently omits.
- `src/TacticusPlanner.GameDomain/ProgressionRules.cs` — gains the
  rank-to-minimum-progression, rank-to-required-level, and
  ability-level-to-minimum-progression tables needed for prerequisite
  detection. Static ordering rules only; no catalog data.
- `src/TacticusPlanner.Api/Features/Goals/` — the combined-creation endpoint's
  per-unit logic is *not* extracted; the import calls the same validation,
  conflict-detection, planning and project services directly.
- Regenerated `artifacts/openapi` artifact — the import response schema
  changes.
- **Depends on** `fix-goal-mutation-isolation-level` (concurrent and repeated
  mutations must not abort) and `fix-goal-ability-cap-effective-progression`
  (a synthesized Ascension must lift the cap for the Ability goal that
  depends on it). Apply both first.
- Companion `tacticus-planner-apps` change of the same name consumes the new
  response, removes the client-side fan-out and snapshot resolution, renders
  the bucketed outcome report, and adds the missing `Ability -> Ascension`
  prerequisite edge. Apply this API change first.
