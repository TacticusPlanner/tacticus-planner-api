## Why

Project priority is currently exposed as a total order of individual `ProjectGoal` memberships, and creation accepts optional numeric goal priorities. The product is moving to unit-centered planning: players prioritize Characters and Machines of War, while goals inside a unit are automatically ordered. The existing profile-wide Active/Paused uniqueness rule also prevents two projects from expressing different targets of the same type for one unit.

Independent equipment-item goals do not fit this model, target no unit, and have no farming-cost engine. V2 is pre-production, so the clean design is to remove them now and later reintroduce equipment as a goal type attached to a Character.

## What Changes

- Add a project unit-order endpoint that accepts ordered Character/MoW keys and atomically rewrites the existing flattened `ProjectGoal.Priority` order.
- Automatically order goals inside each unit: explicit dependencies first, otherwise preserve prior relative order, with stable goal id as the deterministic tie-breaker.
- Remove caller-supplied numeric project priority from single and combined goal creation. New goals join their existing unit block or append a new unit block.
- Replace profile-wide Active/Paused uniqueness with project-scoped uniqueness on `(project, entity type, entity id, goal type)`.
- Enforce the new rule for creation, combined creation, membership changes, resume/unarchive, and project membership replacement, with structured conflict details identifying affected projects and existing goals.
- Add a concurrency-safe database backstop by denormalizing immutable slot identity and an in-flight flag onto `project_goals`, protected by a partial unique index.
- Remove `GoalEntityType.Item`, `GoalType.UpgradeItem`, `GoalConfig.Item`, equipment-specific validation/contracts, and persisted independent equipment goals.
- Rename ordinary Upgrade material target symbols from `UpgradeItemTarget*` to `UpgradeMaterialTarget*`; wire JSON shape remains `targets` with `upgradeId` and `quantity`.
- Regenerate the OpenAPI artifact for coordinated frontend consumption.

## Capabilities

### New Capabilities

- `project-unit-ordering`: Public unit-order semantics and deterministic flattening into scheduler goal order.
- `project-goal-slots`: Project-scoped Active/Paused goal-type uniqueness and structured conflict behavior.
- `goal-target-model`: Supported goal entities become Character/MoW only; independent equipment goals are removed while ordinary unit Upgrade goals remain.

## Impact

- **Domain:** goal entity/type/config enums and records; `ProjectGoal` gains denormalized slot fields used for database enforcement.
- **Persistence:** destructive V2 migration deletes Item/UpgradeItem goals, drops the profile-wide unique index, backfills membership slot data, and adds a partial unique project-slot index.
- **API:** create/combined-create, status transitions, goal membership, project membership/order, list responses, validation, conflict mapping, and a new unit-order endpoint.
- **OpenAPI:** generated artifact changes; coordinated apps change has the same name in `tacticus-planner-apps`.
- **Tests:** endpoint/invariant tests plus PostgreSQL-specific uniqueness coverage where EF InMemory cannot exercise partial indexes.
- **V1/game catalog/player data:** unchanged. Equipment catalog/player data remain; only independent V2 goals targeting equipment are removed.
