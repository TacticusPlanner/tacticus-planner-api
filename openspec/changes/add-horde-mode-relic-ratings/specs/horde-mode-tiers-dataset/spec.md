## ADDED Requirements

### Requirement: Horde-mode characters may carry relic-conditional multipliers

Each `horde-mode-tiers` character entry MAY carry `relics`, served as:

```
relics?: { equipmentId: string,   // a catalog relic id the character may equip; ascending by equipmentId
           multiplier: number }[] // in (0, 5], ≥ the character's base effective multiplier; omitted when empty
```

The served `relics` SHALL equal the raw list sorted by `equipmentId` (ordinal), omitted when missing or empty. The server SHALL NOT decide whether a relic applies: the client uses a relic's `multiplier` in place of the character's base effective multiplier while the synced roster has that relic equipped on that character, at any level, and the highest such multiplier when several apply.

Assumptions:

- A relic counts as equipped at any relic level; level scaling is not modelled.
- Relic values are curated estimates, not measured clear rates.

#### Scenario: Seed relic rating

- **WHEN** the client reads `horde-mode-tiers`
- **THEN** `tauCrisis` (S, multiplier 1.92) carries `relics` `[{ equipmentId: "R_Crit_Dw02AdvancedBurstCannon", multiplier: 1.98 }]` and `ultraCalgar` has no `relics` field

#### Scenario: Relics are sorted

- **GIVEN** the raw file lists a character's relics as `R_Crit_Lakrimae` then `R_Block_DaemonfleshPlate`
- **WHEN** the catalog is built
- **THEN** they are served as `R_Block_DaemonfleshPlate` then `R_Crit_Lakrimae`, and the hash equals the reverse authoring order's

### Requirement: Relic ratings are validated on load

The catalog loader SHALL reject the raw `horde-mode-tiers` file, with an `InvalidHordeModeTiers` error naming the character and the offending value, when a relic's `equipmentId` is not a catalog equipment id with `isRelic` true, the relic's `allowedUnits` does not include the character, the same `equipmentId` appears twice for one character, or the relic `multiplier` is not in (0, 5] or is below the character's base effective multiplier.

#### Scenario: Relic the character cannot equip

- **GIVEN** `ultraCalgar` lists `R_Crit_Lakrimae`
- **WHEN** the catalog loads
- **THEN** loading fails naming `ultraCalgar` and `R_Crit_Lakrimae`

#### Scenario: Not a relic

- **GIVEN** `tauCrisis` lists an equipment id that is not a relic
- **WHEN** the catalog loads
- **THEN** loading fails naming `tauCrisis` and that id

#### Scenario: Relic lowers the rating

- **GIVEN** `tauCrisis` has multiplier 1.92 and lists `R_Crit_Dw02AdvancedBurstCannon` with multiplier 1.5
- **WHEN** the catalog loads
- **THEN** loading fails naming `tauCrisis` and 1.5
