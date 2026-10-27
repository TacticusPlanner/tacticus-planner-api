## MODIFIED Requirements

### Requirement: The catalog serves one consolidated `shops` dataset

The game catalog SHALL serve a dataset keyed `shops` as a plain array with one record per daily shop,
each identified by a stable, human-readable `id` (`guild`, `war`, `rogue-trader`, `crusade`). It SHALL
be built from four merchants of the pinned datamine copy `gameconfig/shop/merchants.json` (`guild` →
`guild`, `guildWars` → `war`, `elderShop` → `rogue-trader`, `crusadeShop` → `crusade`), SHALL fail startup
naming the shop when one of them is missing, and SHALL be the only shop dataset exposed publicly. The catalog SHALL keep no hand-kept shop files.

#### Scenario: Shops dataset is served without authentication

- **WHEN** a client requests the `shops` endpoint
- **THEN** an array with one record per daily shop is returned without requiring authentication, consistent with the rest of the game catalog

#### Scenario: Shops dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `shops`

#### Scenario: Raw per-shop sources have no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving a single raw per-shop source file directly

#### Scenario: All four daily shops are present

- **WHEN** the `shops` dataset is served
- **THEN** it contains exactly the records with ids `guild`, `war`, `rogue-trader`, and `crusade`

## REMOVED Requirements

### Requirement: Shop records carry no display text or icon

**Reason**: D1 — restates the catalog-wide ids-only rule.

**Migration**: `game-catalog-http-caching` — "Every served dataset is public and listed in the manifest".

### Requirement: Shop events are out of scope

**Reason**: D3 — "The catalog serves one consolidated `shops` dataset" already limits the dataset to one record per daily shop, and its scenario "All four daily shops are present" asserts exactly `guild`, `war`, `rogue-trader`, `crusade`.

**Migration**: See `game-shops-dataset` — "The catalog serves one consolidated `shops` dataset".

### Requirement: Adding the shops dataset does not bump the schema version

**Reason**: L6 — one-off transition rule that only applied while the dataset was added.

**Migration**: None; the manifest entry is covered by `game-catalog-http-caching` — "Every served dataset is public and listed in the manifest".
