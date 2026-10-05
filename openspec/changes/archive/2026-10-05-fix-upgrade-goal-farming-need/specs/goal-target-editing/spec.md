## ADDED Requirements

### Requirement: Editing an Upgrade target can replace its ranges

`PUT /me/goals/{goalId}/target` for an Upgrade goal SHALL accept optional `rankRange`,
`activeRange` and `passiveRange` groups inside `target.upgrade`, validated exactly as at creation.
The Upgrade group SHALL be replaced as a whole: a request that omits a range group clears it. The
`TargetChanged` event SHALL record the previous and new ranges together with the previous and new
`targets`, and an edit that changes only a range SHALL count as a change.

#### Scenario: Range edited, targets unchanged

- **GIVEN** an Active Upgrade goal with `rankRange { Stone2, Stone4 }`
- **WHEN** its owner submits the same `targets` with `rankRange { Stone2, Stone5 }` and the
  current revision
- **THEN** the goal returns the new range, `revision` increments and a `TargetChanged` event
  records the old and new range

#### Scenario: Omitting a range clears it

- **WHEN** the owner submits `target.upgrade` with `targets` and no range groups
- **THEN** all three range groups become null

#### Scenario: Invalid edited range is rejected

- **WHEN** the submitted range violates the creation rules
- **THEN** the edit is rejected with the same validation error as creation and nothing changes
