## ADDED Requirements

### Requirement: Each unit exposes its quest-unit reference id

Each `bosses` / `primes` entry SHALL carry `questUnitId` — a single npc id string
copied verbatim from the raw unit set's `questUnitId` — when the source provides
it, and SHALL omit the field entirely when the source does not. It is a
structural identity field only: the catalog SHALL NOT resolve it to a display
name, portrait, or icon path. An absent `questUnitId` SHALL NOT fail the build;
not every unit set defines one.

#### Scenario: Quest-unit id passes through when present

- **WHEN** a raw unit set carries `questUnitId: "tyranNpc3Termagant"`
- **THEN** the served entry has `questUnitId` equal to `"tyranNpc3Termagant"` and no npc name, portrait, or icon field alongside it

#### Scenario: Quest-unit id omitted when absent

- **WHEN** a raw unit set has no `questUnitId`
- **THEN** the served entry has no `questUnitId` field and the build still succeeds
