## Why

Players plan projects toward a deadline: a Battle Pass, Guild Raid or Guild War season, a specific Legendary Event run, or a date they pick (UserJot [Plan goals against an event or deadline](https://tacticusplanner.userjot.com/board/p/plan-goals-against-an-event-or-deadline), In Progress, with the merged [Enhancements for Project Organization and Goal Management](https://tacticusplanner.userjot.com/board/p/enhancements-for-project-organization-and-goal-management)). Today a project's target can only name a Legendary Event (`add-project-event-target`), with no run and no other kind. Companion of `tacticus-planner-apps` change `add-project-deadline-targets`; this API half applies first.

## What Changes

- `ProjectTargetKind` gains `battlePass`, `guildRaidSeason`, `guildWarSeason` and `customDate` next to `legendaryEvent`.
- The target wire shape grows from `{ kind, eventId }` to `{ kind, eventId, run, season, date }`. Each field other than `kind` is optional and nullable, and which fields are allowed depends on `kind`.
  - `legendaryEvent`: `eventId` (a served `lres` id, required) and `run` (1, 2, 3, or null for "next run").
  - Season kinds: `season` (a positive integer pinning one season, or null for rolling).
  - `customDate`: `date` (`YYYY-MM-DD`, required).
- The server stores and validates the target only. It never resolves a deadline instant; the client derives that from the catalog event definitions.
- EF migration: three nullable columns, `TargetRun` (int), `TargetSeason` (int) and `TargetDate` (date). Existing rows keep `TargetKind`/`TargetEventId` and read as `run` null. No backfill.
- The staging transfer bundle carries the three new fields.

No **BREAKING** changes: a client that sends `{ kind: "legendaryEvent", eventId }` keeps working and gets `run: null`, `season: null`, `date: null` back.

## Capabilities

### Modified Capabilities

- `project-event-target`: a target can be a Legendary Event run, a season (rolling or pinned), or a date.

## Impact

- `TacticusPlanner.Domain/Projects` (`ProjectTargetKind`, `Project`), `Features/Projects` (`ProjectTargets`, create/update validators, mapper), `ProjectConfiguration` and a migration, `Features/StagingTransfer` (bundle, exporter, importer), endpoint tests, regenerated `artifacts/openapi`.
- Companion: `tacticus-planner-apps` `add-project-deadline-targets` (form, chip, deadline resolver), whose shared contract surface is `POST/PUT me/projects` and the project responses' `target`.
