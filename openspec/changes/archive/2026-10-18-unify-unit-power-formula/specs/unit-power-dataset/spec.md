## Purpose

Defines the served `unit-power` catalog dataset: the game's power tables, progression multipliers and per-unit power fields that the client's in-game power formula reads in addition to the unit, equipment and upgrade datasets, how it is derived from its raw file, and how it is validated.

## ADDED Requirements

### Requirement: The catalog serves one unit-power record

The catalog SHALL serve a dataset `unit-power` containing exactly one record with these fields (all numbers are integers):

```
{
  id: "unit-power",
  heroProgressionSteps: {                      // exactly 20, step 0..19 ascending
    step: number,                              // the progression index (rarity + stars, 0..19)
    unitStatMultiplierPct: number,             // > 0
    abilityPowerMultiplier: number             // > 0
  }[],
  mowProgressionSteps: {                       // exactly 20, step 0..19 ascending
    step: number,
    abilityPowerMultiplier: number,            // > 0
    mythicAbilityPower: number                 // >= 0; 0 when the step unlocks no mythic ability
  }[],
  damageProfileModifiers: { damageType: string, modifier: number }[],   // ascending by damageType
  traitPowerModifiers: { traitId: string, modifier: number }[],         // ascending by traitId
  abilityPowerModifiers: { abilityId: string, modifier: number }[],     // ascending by abilityId
  abilityPowerCurves: {
    active: number[],                          // index = ability level − 1
    passive: number[],
    relic: number[]                            // index = relic level − 1
  },
  units: {                                     // ascending by unitId; every catalog character and MoW
    unitId: string,
    powerMultiplier: number,                   // percent; 100 when the raw entry has none
    rankUpStatIncreases: number[][]            // characters: one row of 6 per rank, aligned with the
                                               // unit's rankUpUpgrades rows and upgradeIds order;
                                               // MoWs: []
  }[]
}
```

Each modifier is a percentage. The server sends ids and numbers only: no display strings, names or icons. The dataset SHALL be listed in the public manifest with its content hash.

What the client derives, not the server: a unit's stats at a rank, progression and set of applied upgrades and items; the progression index from rarity and stars; the power of a character, MoW or NPC.

Derivation from the raw `unit-power.json` (V1's GameConfig slice shape):

- `heroProgressionSteps[i]` = raw `heroProgressionSteps[i]`, keeping only `unitStatMultiplierPct` and `abilityPowerMultiplier`. Per-unit progression overrides are not served, because every current override changes only costs.
- `mowProgressionSteps[i]` = raw `heroProgressionStepsMoW[i]`, keeping `abilityPowerMultiplier`, with `mythicAbilityPower` set to 0 when it is absent.
- `damageProfileModifiers`, `traitPowerModifiers` = the raw objects as sorted lists. `abilityPowerModifiers` = each raw entry's `baseMultiplier`, as a sorted list.
- `abilityPowerCurves` = the raw `abilityPowerCurve` arrays, unchanged.
- `units[]` = one entry per raw `lineup` unit that is a catalog character or MoW. `powerMultiplier` is the raw value, or 100 when absent. `rankUpStatIncreases` is the raw `upgradesStatIncrease`, truncated to the unit's catalog `rankUpUpgrades` row count, or `[]` for a MoW.

Assumptions:

- The raw file is V1's bundled slice of Snowprint's `clientGameConfig`, refreshed each game patch. Its values were confirmed in-game by V1 for worldKharn (936,827) and orksWarboss (1,037,811).
- Base Health, Damage and Armour, weapons, movement, traits, ability ids and rank-up upgrade ids are served on `units-*`. Equipment level stats are served on `equipment-*`. Each upgrade's stat type is served on `upgrades-*`. As of the 1.43 data these match the V1 slice exactly, except that the V1 slice also carries traits `Hero` and `MachineOfWar`, which both have modifier 100.

#### Scenario: Shape for a character

- **WHEN** the client reads `unit-power`
- **THEN** the `ultraTigurius` entry has `powerMultiplier` 100 and 20 `rankUpStatIncreases` rows (one per catalog `rankUpUpgrades` row), and its Gold1 row (index 12) is `[122, 122, 56, 56, 26, 26]`, aligned with `upgHpL120C, upgHpL011C, upgDmgL005C, upgDmgL004C, upgArmL004C, upgArmL010C`
- **AND** `heroProgressionSteps[9]` is `{ step: 9, unitStatMultiplierPct: 160, abilityPowerMultiplier: 5 }`
- **AND** `traitPowerModifiers` holds `{ traitId: "Psyker", modifier: 125 }`, `abilityPowerModifiers` holds `{ abilityId: "StormOfWrath", modifier: 120 }`, and `damageProfileModifiers` holds `{ damageType: "Psychic", modifier: 191 }`

#### Scenario: Shape for a MoW

- **WHEN** the client reads `unit-power`
- **THEN** the `tyranBiovore` entry has `rankUpStatIncreases` `[]`, and `mowProgressionSteps[19]` is `{ step: 19, abilityPowerMultiplier: 29, mythicAbilityPower: 150000 }`

#### Scenario: Missing power multiplier defaults to 100

- **GIVEN** the raw lineup entry for `ultraTigurius` has no `powerMultiplier`
- **WHEN** the catalog is built
- **THEN** the served entry has `powerMultiplier` 100

#### Scenario: Listed in the manifest

- **WHEN** the client reads the public manifest
- **THEN** it contains a `unit-power` entry with a content hash

### Requirement: The unit-power record is validated on load

The catalog loader SHALL reject the raw file with a validation error that names `unit-power` and the offending id or value when:

- either progression list does not have exactly 20 steps, or has a multiplier that is not positive;
- a catalog character or MoW has no `lineup` entry;
- a character's `upgradesStatIncrease` has fewer rows than its catalog `rankUpUpgrades`, or a row whose length differs from that rank's `upgradeIds`;
- a value is negative;
- a `rankUpUpgrades` upgrade id has no catalog upgrade with a stat type of Health, Damage or Armour;
- a non-empty catalog character or NPC weapon damage type has no `damageProfileModifiers` entry;
- the `active` or `passive` curve is shorter than the highest ability level `character-ability-costs` reaches (its last `lvl` + 1, 60 today);
- a catalog relic has more levels than the `relic` curve.

Raw `lineup` entries that are not catalog characters or MoWs (unreleased units) SHALL be skipped without error. A catalog trait with no modifier, or a catalog ability with no modifier, is valid and means 100.

#### Scenario: New character without power data

- **GIVEN** a catalog character `newHero` with no `lineup` entry in `unit-power.json`
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `unit-power` and `newHero`

#### Scenario: Misaligned stat increases

- **GIVEN** `ultraTigurius`'s raw Stone1 `upgradesStatIncrease` row has 5 numbers and its catalog Stone1 row has 6 upgrade ids
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `unit-power`, `ultraTigurius` and `Stone1`

#### Scenario: Unknown damage type

- **GIVEN** a catalog NPC whose melee damage type is `Warp` and no `Warp` modifier
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `unit-power` and `Warp`
