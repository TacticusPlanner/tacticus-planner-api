# Tasks

Paths are under `src/` unless stated. Build and test commands: see this repo's AGENTS.md.

## 1. Plan write validation

- [ ] 1.1 Add `MinMembers = 3` next to `MaxMembers` in `TacticusPlanner.Domain/LegendaryEvents/LegendaryEventValidation.cs` and change `ValidateUnits` in `TacticusPlanner.Api/Features/LegendaryEventPlans/LegendaryEventCatalogValidator.cs` to fail with `memberUnitIds` and the message "A team has 3–5 members." when the member count is outside 3..5 (spec: every team write is validated; the reserve does not count). Verify: `LegendaryEventCatalogValidatorTests` covers 2 members (rejected), 2 members plus a reserve (rejected), 3 (accepted), 5 (accepted) and 6 (rejected).
- [ ] 1.2 Check that create, update, depth, reorder and delete endpoints behave as the spec says for a stored short team: reorder and delete succeed; an update or depth write that keeps it below three is rejected with 400 naming `memberUnitIds`; completing it to three succeeds. Verify: endpoint tests seed a two-member team directly in the database and assert each outcome (read 200 with the team, reorder 200, delete 200, update with two members 400, update with three members 200).
- [ ] 1.3 Update every existing test fixture that creates a plan team with one or two members to use three or more. Verify: `dotnet test` shows no failure caused by the new minimum.

## 2. V1 import

- [ ] 2.1 In `Features/V1Import/V1LegendaryEventImportService.cs` skip a team left with one or two resolvable units with issue `team_too_small` (value = remaining count), keep `empty_team` for zero, and add the code to the documented code list near line 443 (spec: teams, units and objectives are resolved by the documented rules). Verify: `V1LegendaryEventImportEndpointTests` asserts a two-unit team is skipped with `team_too_small` value 2, a three-unit team with one disallowed unit is skipped with `unit_not_allowed_on_lane` and `team_too_small` value 2, and a three-unit team imports.
- [ ] 2.2 Regenerate the OpenAPI artifact if the build changes it and note the new issue code for the apps. Verify: the build leaves `artifacts/openapi` unchanged or committed.

## 3. Integration

- [ ] 3.1 Run the full build and tests. Verify: `dotnet build` and `dotnet test` succeed.
- [ ] 3.2 On the local Aspire stack call `POST …/teams` with two members and with three members for a profile. Verify: 400 naming `memberUnitIds` then 200.

## Workflow follow-up

- Deploy this half before the apps half; archive both halves together after the apps half is verified on staging.
