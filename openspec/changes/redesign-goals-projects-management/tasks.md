## 1. Remove independent equipment goals and clarify material targets

- [x] 1.1 Remove `GoalEntityType.Item`, `GoalType.UpgradeItem`, `GoalConfig.Item`/`ItemTarget`, equipment lookup/validation branches, mapper branches, and equipment-goal endpoint tests.
- [x] 1.2 Rename `UpgradeItemTarget` domain/request/response symbols to `UpgradeMaterialTarget` equivalents while preserving `targets[].upgradeId/quantity` wire fields.
- [x] 1.3 Update endpoint summaries, validators, serialization tests, and all exhaustive switches for the supported Character/MoW model.
- [x] 1.4 Add regression tests for Character and MoW Upgrade goals after the rename.

## 2. Add project-scoped goal-slot persistence

- [x] 2.1 Extend `ProjectGoal` and configuration with required denormalized entity type/id/goal type plus `OccupiesInFlightSlot`.
- [x] 2.2 Add a migration that deletes Item/UpgradeItem goals, adds/backfills slot columns, drops the profile-wide goals index, and creates the partial unique project-slot index.
- [x] 2.3 Centralize membership construction and in-flight flag synchronization so all write paths copy immutable slot identity and lifecycle transitions update every membership.
- [x] 2.4 Replace `GoalConflictDetection` with project-slot constraint mapping and structured conflict response types.
- [ ] 2.5 Add PostgreSQL-backed migration/index tests for backfill, preservation of ordinary goals, cascade deletion, and racing unique-slot writes; retain InMemory handler tests.

## 3. Enforce slots across API operations

- [x] 3.1 Update single and combined creation to resolve target projects before conflict detection and validate every requested unit/type slot atomically.
- [x] 3.2 Update `UpdateGoalProjectsEndpoint` to reject newly occupied project conflicts and return structured details.
- [x] 3.3 Update `UpdateProjectGoalsEndpoint` to validate the complete requested membership set, prevent orphaning, populate slot fields, and reject duplicates atomically.
- [x] 3.4 Update `UpdateGoalStatusEndpoint` so entering Active/Paused validates all memberships and toggles occupancy; leaving in-flight status frees all slots.
- [ ] 3.5 Add endpoint tests for separate instances in different projects, same-project conflict, Completed/Archived reuse, shared canonical goals, combined requests, membership changes, resume, stale/racing conflicts, and ownership isolation.

## 4. Implement unit ordering

- [x] 4.1 Add request/response records and validation for the dedicated project unit-order endpoint.
- [ ] 4.2 Lock the project aggregate during reorder/membership mutations, validate an exact distinct Active/Paused unit set, and reject stale sets atomically.
- [x] 4.3 Implement stable dependency-first within-unit ordering and flatten submitted unit order into spaced `ProjectGoal.Priority` values; retain historical memberships afterward in stable order.
- [x] 4.4 Remove numeric `Priority` from single/combined goal project-selection contracts and automatically insert new goals into an existing/new unit block.
- [ ] 4.5 Add tests for multi-goal units, Character/MoW mixing, dependency order, unrelated stability, new goals, stale sets, duplicates, empty/one-unit projects, historical goals, authorization, and concurrent membership/reorder.

## 5. Contract and verification

- [x] 5.1 Build the API to regenerate `artifacts/openapi` and verify equipment discriminators/config are absent, material target names are clear, structured conflicts are exposed, and unit ordering is documented.
- [x] 5.2 Coordinate generated client changes with `tacticus-planner-apps/openspec/changes/redesign-goals-projects-management` before merge.
- [ ] 5.3 Apply/reset the migration through the Aspire `api-migrations` resource and verify a populated local database containing ordinary and equipment goals.
- [x] 5.4 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, `docker build -f src/TacticusPlanner.Api/Dockerfile -t tacticus-planner-api:local .`, and `git diff --check`.
