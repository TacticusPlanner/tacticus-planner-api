# Direct scenario edits

OpenSpec rejects a MODIFIED block that drops or renames a scenario the main spec still has
(`specs-apply`: "current spec contains scenario(s) not present in the modified block"), and has no
syntax for removing one scenario. These edits are therefore made by hand during apply, before archive:
remove or rename each scenario in `openspec/specs/<capability>/spec.md` AND in the matching MODIFIED
block under `specs/<capability>/spec.md` (when one exists), so archive then applies cleanly.
Audit ids are in `audit.md`.

## character-ability-costs-dataset

- `The catalog serves a `character-ability-costs` dataset`
  - drop scenario "Dataset is served without authentication"
  - drop scenario "Dataset appears in the manifest"
  - drop scenario "The raw source has no direct endpoint"

## mythic-quests-dataset

- `The catalog serves a `mythic-quests` dataset`
  - drop scenario "Dataset is served without authentication"
  - drop scenario "Dataset appears in the manifest"
  - drop scenario "The raw source has no direct endpoint"

## npcs-dataset

- `The catalog serves one flat `npcs` dataset built from the per-faction raw sources`
  - drop scenario "Npcs dataset is served without authentication"
  - drop scenario "Npcs dataset appears in the manifest"

## raid-bosses-dataset

- `The catalog serves one consolidated `raid-bosses` dataset`
  - drop scenario "Raid-bosses dataset is served without authentication"
  - drop scenario "Raid-bosses dataset appears in the manifest"

## game-shops-dataset

- `The catalog serves one consolidated `shops` dataset`
  - drop scenario "Shops dataset is served without authentication"
  - drop scenario "Shops dataset appears in the manifest"
  - drop scenario "Raw per-shop sources have no direct endpoint"

## game-events-calendar-dataset

- `event-definitions and events-calendar are served; event-occurrences is not`
  - drop scenario "Served datasets appear in the manifest"
  - drop scenario "Served dataset is served without authentication"

## unit-power-dataset

- `The catalog serves one unit-power record`
  - drop scenario "Listed in the manifest"

## horde-mode-tiers-dataset

- `The catalog serves one horde-mode tier list`
  - drop scenario "Listed in the manifest"

## home-screen-event-scoring

- `Scoring data is derived from the V1 datamine and validated`
  - drop scenario "Unknown variant on an occurrence fails the build"

## goal-combined-edit

- `Each section is validated exactly as its own endpoint validates it`
  - drop scenario "Project slot check sees the new target"

## rank-milestones

- `Rank target identity is the normalized end state`
  - drop scenario "Different targets are distinct"

## project-goal-slots

- `In-flight uniqueness is scoped to the profile`
  - drop scenario "Same project rejects duplicate"
- `Every membership and lifecycle path enforces slots`
  - drop scenario "Resume conflicts in one membership"
- `The profile lock serializes slot checks`
  - drop scenario "Racing creates target one slot"

## project-bulk-membership-replacement

- `Batch validation is atomic and actionable`
  - drop scenario "Occupied slot"

## default-project

- `No active-project selection is exposed`
  - drop scenario "The activate operation is gone"

## guild-raid-meta-dataset

- `Boss Meta recommendations provide exact teams`
  - drop scenario "A boss has Meta and alternate exact teams"

## v1-goal-import

- `Missing prerequisites are created automatically by the same rules as manual creation`
  - drop scenario "A created prerequisite precedes its dependents in the import sequence"
  - drop scenario "Rank and Ability imports do not synthesize Level"

## legendary-event-plans

- `One revision per plan guards every mutation`
  - drop scenario "Stale revision on a run write"
- `Teams belong to one lane of the plan`
  - rename scenario "A short team written earlier stays readable" to "A short stored team stays readable"

## game-catalog-datamine-source

- `Only allowlisted files are copied, unchanged`
  - drop scenario "The seeded allowlist"

## goal-target-model

- `FarmingLocationIds retains its upgrade-node role`
  - rename scenario "Rank goal farming override is unchanged" to "Rank goal farming override round-trips"
- `Upgrade goals carry an optional progression range`
  - rename scenario "Existing and range-less goals are unchanged" to "Range-less goal"

## mows-dataset

- `Each MoW carries its mythic ability`
  - drop scenario "The manifest hash changes"
- `MoWs are read from the pinned datamine`
  - drop scenario "Same MoWs as before"

## ascension-costs-dataset

- `The ascension ladder is built from the pinned datamine file`
  - drop scenario "Same ladder as before"

## characters-dataset

- `Characters are read from the pinned datamine`
  - drop scenario "Same characters as before"

## equipment-dataset

- `Equipment is built from the pinned datamine `items.json``
  - drop scenario "Same items as before"
- `Equipment is served in type, then id order`
  - drop scenario "The migration moves only the order"

## mow-upgrade-costs-dataset

- `The MoW cost ladder is built from the pinned datamine file`
  - drop scenario "Same ladder as before"

## upgrades-dataset

- `Upgrade materials are built from the pinned datamine files`
  - drop scenario "Same materials plus the game's new one"

## openapi-contract

- `Enum wire values are stable`
  - rename scenario "Response values are unchanged" to "Response values are PascalCase"

## goal-target-milestones

- `Target keys are defined per goal type`
  - rename scenario "Rank key is unchanged" to "Rank key"

## legendary-event-lane-points

- `A lane's killPoints is its defeat-all base`
  - rename scenario "Other events are unchanged" to "Other events"
