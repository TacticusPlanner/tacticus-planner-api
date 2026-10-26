## MODIFIED Requirements

### Requirement: The catalog serves one consolidated `shops` dataset

The game catalog SHALL serve a dataset keyed `shops` as a plain array with one record per daily shop,
each identified by a stable, human-readable `id` (`guild`, `war`, `rogue-trader`, `crusade`). It SHALL
be built from four merchants of the pinned datamine copy `gameconfig/shop/merchants.json` (`guild` →
`guild`, `guildWars` → `war`, `elderShop` → `rogue-trader`, `crusadeShop` → `crusade`), SHALL fail startup
naming the shop when one of them is missing, and SHALL be the only shop dataset exposed publicly; the raw
merchants file SHALL NOT be served directly. The catalog SHALL keep no hand-kept shop files.

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
