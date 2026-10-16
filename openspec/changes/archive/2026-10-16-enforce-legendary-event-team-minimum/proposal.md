# Proposal

API half of the paired change `enforce-legendary-event-team-minimum` (companion: `tacticus-planner-apps`, same name). This half applies first.

## Why

A Legendary Event team cannot have fewer than three characters in the game, but the plan API accepts 1 to 5 members and the V1 import brings in any size. Short teams then flow into the token plan, clear estimates, suggestions and the master table as if they could be played. On the staging account 1 of 48 teams (Uthar Alpha, imported, two members, stored depth 5) is below the minimum; the apps' own specs treat partial teams as first-class, which is what this change reverses.

## What Changes

- The plan API requires 3 to 5 non-reserve members on team create and update (400 naming `memberUnitIds` otherwise); the reserve does not count.
- Teams already stored with fewer than three members stay readable, reorderable and deletable; any write that leaves such a team below three is rejected, so editing it forces completion.
- The V1 legendary event import skips a team left with one or two resolvable units (`team_too_small`, value = the remaining count) alongside the existing `empty_team` skip.
- No schema or migration change, no contract change to the served plan shape.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `legendary-event-plans`: team size is 3 to 5; write validation rejects other sizes; legacy short teams stay readable and deletable.
- `v1-legendary-event-import`: a team with one or two resolvable units is skipped with `team_too_small`.

## Impact

- `src/TacticusPlanner.Domain/LegendaryEvents/LegendaryEventValidation.cs` (new `MinMembers = 3`), `src/TacticusPlanner.Api/Features/LegendaryEventPlans/LegendaryEventCatalogValidator.cs` (`ValidateUnits`), `src/TacticusPlanner.Api/Features/V1Import/V1LegendaryEventImportService.cs` (skip rule and issue code, with its documented code list).
- Tests: `tests/TacticusPlanner.Api.Tests/LegendaryEventCatalogValidatorTests.cs` and `V1LegendaryEventImportEndpointTests.cs`; plan endpoint tests that create short teams as fixtures.
- Persisted data: no migration; the one existing short team is handled by the apps (flagged, ignored by calculations, completed or deleted by the user).
- Companion apps change: editor, suggestions, incomplete-team handling and the import result copy for `team_too_small`.
