## Context

After `revise-horde-mode-tier-list`, each `horde-mode-tiers` character has a tier and its own multiplier. Relics are already catalog equipment (`Data/equipment/*.json`) with ids such as `R_Block_DaemonfleshPlate`, Mythic rarity and `allowedUnits`. The synced roster lists each character's `equippedItems` (`slotId`, `equipmentId`, `level`), so the client can tell whether a relic is equipped.

## Goals / Non-Goals

**Goals:** serve relic-conditional multipliers, validated against the catalog's relics.

**Non-Goals:** relic level scaling (any level counts as equipped), relic advice text (that is `add-horde-mode-strategy-notes`), relics on untiered characters.

## Decisions

- **Field on the character entry, not a separate dataset.** `relics` sits next to the base `multiplier`, so the whole rating is in one record and one hash.
- **The relic multiplier replaces the base, it does not stack.** With several equipped relics listed (none today), the highest applies. The client applies this; the server serves values only.
- **A relic multiplier must be ≥ the character's base effective multiplier.** A relic never lowers a rating.
- **Validation reads the equipment catalog.** `ValidateHordeModeTiers` gains the relic ids with their `allowedUnits`; an id whose equipment is not `isRelic` or does not allow that character is rejected.
- **Values for "Potential S/A".** Severyn's notes give exact values for four relics and "potential S" or "potential A" for four. Those default to the bottom of the next tier's range (S 1.80, A 1.60), to be calibrated later.
- **Raw dataset and model change.** The denormalizer is unchanged apart from passing `relics` through, served sorted by `equipmentId` (ordinal) so authoring order does not change the hash; an empty or missing list is omitted. `SchemaVersion` bumps. No EF Core migration.

## Seed relic ratings

| Character | unitId | Base | Relic | equipmentId | Relic multiplier |
| --- | --- | --- | --- | --- | --- |
| Re'vas | tauCrisis | S 1.92 | DW-02 Advanced Burst Cannon | R_Crit_Dw02AdvancedBurstCannon | 1.98 |
| Maladus | deathBlightlord | S 1.90 | Daemonflesh Plate | R_Block_DaemonfleshPlate | 1.96 |
| Angrax | blackTerminator | A 1.76 | Daemonflesh Plate | R_Block_DaemonfleshPlate | 1.82 |
| Snotflogga | orksRuntherd | S 1.83 | Supa-Cybork Body | R_Defensive_SupaCyborkBody | 1.92 |
| Anuphet | necroOverlord | A 1.66 | Phasal Subjugator | R_Booster_Crit_PhasalSubjugator | 1.80 (potential S) |
| Nicodemus | bloodSanguinary | A 1.69 | Chalice of Baal | R_Booster_Crit_ChaliceOfBaal | 1.80 (potential S) |
| Typhus | deathTyphus | A 1.63 | Lakrimae | R_Crit_Lakrimae | 1.80 (potential S) |
| Abaddon | blackAbaddon | B 1.40 | Talon of Horus | R_Crit_TalonOfHorus | 1.60 (potential A) |

Every relic's `allowedUnits` includes its character (checked in `Data/equipment`).

## Risks / Trade-offs

- The relic values are preliminary. Mitigation: they are data, recalibrated without code changes.
