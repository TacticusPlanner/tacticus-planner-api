## MODIFIED Requirements

### Requirement: The catalog serves one consolidated `shops` dataset

The game catalog SHALL serve a dataset keyed `shops` as a plain array with one record per daily shop,
each identified by a stable, human-readable `id` (`guild`, `war`, `rogue-trader`, `crusade`). It SHALL
be built from four merchants of the pinned datamine copy `gameconfig/shop/merchants.json` (`guild` →
`guild`, `guildWars` → `war`, `elderShop` → `rogue-trader`, `crusadeShop` → `crusade`), SHALL fail startup
naming the shop when one of them is missing, and SHALL be the only daily-shop dataset exposed publicly.
Seasonal event merchants SHALL NOT appear in `shops`; they are served by the separate `event-shops`
dataset (see `game-event-shops-dataset`), which reuses this dataset's variant projection. The catalog
SHALL keep no hand-kept daily-shop files.

#### Scenario: All four daily shops are present

- **WHEN** the `shops` dataset is served
- **THEN** it contains exactly the records with ids `guild`, `war`, `rogue-trader`, and `crusade`

#### Scenario: Event merchants stay out of `shops`

- **WHEN** `merchants.json` contains event merchants such as `October2026EventShop`
- **THEN** the `shops` dataset still contains exactly the four daily shop records
