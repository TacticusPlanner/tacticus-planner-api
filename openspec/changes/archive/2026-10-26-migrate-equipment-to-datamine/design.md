# Design

## Context

The datamine `gameconfig/items.json` is a dictionary keyed by item id. Each item carries `name`, `itemType` (`I_Block`, `I_Booster_Block`, `I_Booster_Crit`, `I_Crit`, `I_Defensive`), `rarity`, `levels[]` (`stats` plus `goldCost`, `dustCost`, `mythicDustCost`), and, only when set, `abilityId`, `isRelic`, `isUniqueRelic`, `allowedUnits`, `allowedFactions`. The retired catalog files were a V1 port of the same data with stat names spelled out and costs moved to a per-rarity table.

A field-by-field comparison at `v1.43.157.2989` against the retired files: same 217 ids, same type, rarity, name, ability, relic flags, allowed units and factions, and identical level stats after the five renames. Each item's level costs equal the retired `equipment-upgrade-costs.json` ladder for its rarity (`dustCost`→`salvageCost`, `mythicDustCost`→`mythicSalvageCost`, absent = 0). Dumping the served `equipment` view from `main` and from this change gives the same 217 objects; sorted by (type, id) the old list equals the new one, and 18 relic positions differ.

## Decisions

### D1. Keep the served shape; rename the game's stat names back

The roadmap allows either keeping the served shape (hash diff as the regression check) or renaming to the game's words with a `SchemaVersion` bump and an apps change, never both. The pilot keeps the shape so the hash check is the only moving part. Renaming `critDamage`→`critDmg` etc. to the game's names is left to a later, separate change if wanted. The rename map is one dictionary in the denormalizer; unlisted keys (`critChance`, `blockChance`, `hp`, the `*ChanceBonus` keys) pass through.

### D2. Per-item ladder instead of the per-rarity table

The game prices every item level on the item. Serving each item's own costs removes the join and the hand-kept table. In 1.43 every item of a rarity shares one ladder; a test asserts it so a datamine bump that breaks it is noticed (it would be a real game change, not an error, and the test can then be relaxed).

### D3. Canonical order: type, then id

The JSON dictionary has no meaningful order, so the denormalizer imposes one: types in ordinal order (the same order the retired per-type keys had) and ids in ordinal order within a type. This differs from the retired files only for 18 relics that were appended at the end of two files. Reproducing that hand-made order is not worth it; the hash moves once.

### D4. Drop `globalValues.json`

`itemAscensionCosts` was seeded for this pilot on the assumption it held the upgrade ladder. The per-level costs on each item are the ladder, so nothing reads `globalValues.json` and the allowlist rule ("list a file only when a denormalizer reads it") removes it. A later change that needs it (roadmap step 7 `global-values`) lists it again.

## Risks

- A datamine bump can now change equipment with no hand review. That is the point of the migration; the manifest hash and `EquipmentDenormalizerTests` flag it, and validators still check relic `allowedUnits` against characters.
