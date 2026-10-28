## MODIFIED Requirements

### Requirement: The catalog serves a `character-ability-costs` dataset

The game catalog SHALL serve a dataset keyed `character-ability-costs` as a plain array, one record per ability level a character ability can be raised to, ordered by ascending `level`. It SHALL be built from the pinned datamine copy `gameconfig/units/abilityUpgradeCosts.json`; the catalog SHALL keep no hand-kept copy. The dataset is shared by every character and every ability track, not per character.

#### Scenario: One record per datamine row

- **GIVEN** the pinned `abilityUpgradeCosts.json`
- **WHEN** the catalog loads
- **THEN** `character-ability-costs` holds one record per row, in ascending `level`

## REMOVED Requirements

### Requirement: Records carry no display text or icon

**Reason**: D2 — restates this spec's "Each record is keyed by the level it raises an ability to" (exactly these fields and no others) and the catalog-wide ids-only rule.

**Migration**: See `character-ability-costs-dataset` — "Each record is keyed by the level it raises an ability to" and `game-catalog-http-caching` — "Served records carry ids, not presentation".
