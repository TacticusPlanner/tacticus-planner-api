# goal-bulk-status Specification

## Purpose
One request transitions one or many of the caller's goals to a lifecycle status atomically.

## Requirements

### Requirement: One request transitions many goals

`POST /goals/status` SHALL accept `{ goalIds, status, expectedOrderRevision }` (`goalIds` non-empty and distinct, `status` one of `Active`, `Paused`, `Completed`, `Archived`) and transition every named goal under the single-transition lifecycle rules, leaving goals already in that status unchanged. On success it SHALL return `{ goals, orderRevision }` with every named goal's updated detail.

#### Scenario: Pause three goals

- **GIVEN** Active goals A, B, C at positions 1..3
- **WHEN** the caller posts `{ goalIds [A, C], status Paused }`
- **THEN** A and C are Paused at their positions, B is unchanged, and the response carries A and C with the new `orderRevision`

#### Scenario: Single goal

- **WHEN** the caller posts `{ goalIds [A], status Completed }`
- **THEN** A is Completed, leaves the in-flight order, and the response carries A

#### Scenario: Already in the status

- **GIVEN** A is Paused
- **WHEN** the caller posts `{ goalIds [A], status Paused }`
- **THEN** the response is 200, A's revision is unchanged and the order revision is unchanged

### Requirement: Bulk transitions fail atomically

An unknown or foreign id (404), a disallowed transition for any goal (400 naming the goal), a slot conflict on any goal entering an in-flight status (409 `slotOccupied` naming the goal), or a stale `expectedOrderRevision` (409 `goalOrderStale`) SHALL reject the whole request with no goal changed. `expectedOrderRevision` is required when the transition can change the in-flight set (into or out of Active/Paused from a terminal status) and ignored otherwise.

#### Scenario: One conflicting resume

- **GIVEN** Completed goals A and B, and an in-flight goal occupying B's slot
- **WHEN** the caller posts `{ goalIds [A, B], status Active, expectedOrderRevision r }`
- **THEN** the response is 409 `slotOccupied` naming B and the occupying goal, and A stays Completed

#### Scenario: Foreign id among own goals

- **WHEN** the caller posts own goal A and another profile's goal F
- **THEN** the response is 404 and A is unchanged

#### Scenario: Stale order revision

- **GIVEN** the in-flight set changed since the caller read `orderRevision`
- **WHEN** the caller completes two Active goals with the old revision
- **THEN** the response is 409 `goalOrderStale` with the current order under `current`, and nothing changes
