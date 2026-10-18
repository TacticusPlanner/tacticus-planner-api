## Context

All four served events use the same eight templates (waves with damage, defeat a faction, use abilities with an alliance, waves with a damage type, defeat enemies with an alliance, win without a trait, win without summoning, defeat enemies with a damage type).

## Decisions

### D1. Parse, don't re-author

Raw files keep V1's text so the refresh from the V1 datamine stays a copy. `LreDenormalizer` matches each line against ordered regex templates and emits `kind` values `anyBattle`, `defeatFaction`, `useAbilitiesWithAlliance`, `damageType`, `defeatWithAlliance`, `winWithoutTrait`, `winWithoutSummon`, `defeatWithDamageType`. Faction labels ("Necrons", "AdMechs", "Death Guard", "T'au", "Daemon enemies") map to catalog faction ids through a small alias table; trait and damage-type names map to catalog ids. An unmatched line or unknown alias fails the load.

### D2. Ticks beside counts

`CompletedRegularMissions` / `CompletedPremiumMissions` are `int[]` columns on the run inputs row. Validation: indices within 0..count−1 of the catalog list, distinct, and `regularMissions ≥ completedRegularMissions.length` (same for premium). Absent arrays in a PUT mean empty.

## Risks / Trade-offs

- "Daemon enemies" is a trait-like group rather than a faction; it maps to the catalog's Daemon trait and the client matches it by NPC trait. The alias table documents each such case.
