## Why

The web planner's 1-click Legendary Event plan files the goals it creates in a project that belongs to that event. Projects have no way to say what they relate to. Companion of `tacticus-planner-apps` change `add-project-event-target`; this API half applies first.

## What Changes

- `Project` gains an optional target: `TargetKind` (enum, `LegendaryEvent` only for now) and `TargetEventId` (catalog `lres` id).
- `POST me/projects` and `PUT me/projects/{projectId}` accept `target: { kind, eventId } | null`; list and detail responses return it.
- Validation: `kind` must be known, `eventId` must be a served `lres` id, the Default project cannot have a target.
- EF migration adding the two nullable columns and an index on (`ProfileId`, `TargetKind`, `TargetEventId`).

## Capabilities

### New Capabilities

- `project-event-target`

## Impact

- `TacticusPlanner.Domain/Projects`, `Features/Projects` endpoints, mapper and validators, persistence configuration and migration, endpoint tests.
