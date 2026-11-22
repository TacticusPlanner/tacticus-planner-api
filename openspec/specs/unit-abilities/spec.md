# unit-abilities Specification

## Purpose

Describes the `abilities` field of the served `characters` and `mows` datasets: what each of a unit's abilities
does, what it lands on and whether it attacks in melee or at range. Clients use it to pick teams for ability
objectives (the HRE's ability damage and ability uses) and to filter units.

## Requirements

### Requirement: Every character and MoW serves its abilities

Each served `characters` record SHALL carry `abilities`: its active ability then its passive ability, and each served
`mows` record its primary then secondary ability. Each entry SHALL carry `id` (the ability id), `kind` (`active`,
`passive`, `primary` or `secondary`), `roles` (one or more of `damage`: the ability itself deals damage; `support`:
it heals, repairs, shields, revives or buffs; `utility`: it summons, moves, marks, debuffs, taunts or changes
terrain), `target` (`none`: no target to pick; `single`: one unit; `aoe`: an area or several units) and `attack`
(`melee` or `ranged` when the game data gives the ability that attack range, else null).

#### Scenario: An AoE damage active

- **WHEN** the `characters` dataset is served
- **THEN** `ultraCalgar`'s active has `roles: ["damage"]`, `target: "aoe"` and `attack: "ranged"`

#### Scenario: Actives that deal no damage

- **WHEN** the `characters` dataset is served
- **THEN** the actives of `ultraInceptorSgt` (Bellator, who summons Inceptors) and `ultraApothecary` (Incisus, who
  heals) have no `damage` role (Severyn, 2026-11)

#### Scenario: A MoW

- **WHEN** the `mows` dataset is served
- **THEN** each MoW's `abilities` hold its primary then its secondary ability

### Requirement: The roles and target are curated

The roles and target SHALL come from the curated `unit-abilities` file, keyed by unit id then ability id, drafted from
the game data and ability text and reviewed by hand. The catalog load SHALL fail when a curated unit is not a
character or MoW, when a unit or one of its active or passive abilities has no entry, when an entry names an ability
the unit does not have, or when a role or target is not one of the values above, so a datamine bump that adds a unit
or changes its abilities fails until they are curated. The `attack` SHALL be read from the game data, not curated.

#### Scenario: A new unit

- **GIVEN** a datamine bump that adds a character
- **WHEN** the catalog loads without its entry in `unit-abilities`
- **THEN** the load fails and names the unit
