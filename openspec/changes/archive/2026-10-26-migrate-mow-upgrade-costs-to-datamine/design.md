# Design

## Context

`gameconfig/units/abilityUpgradeCostsMoW.json` is a 59-row array; each row has `gold`, `machinesOfWarToken`, one `abilityToken{Rarity}`, and either `dust` (levels 2 to 8) or one `itemAscensionResource_{Rarity}` (levels 9 and up). V1's `mow-upgrade-costs.json` carried the same values renamed: `salvage`, `badges {rarity, amount}`, `forgeBadges`, `components`. A row-by-row comparison at `v1.43.157.2989` finds no difference.

## Decisions

### D1. Keep the served names

The roadmap rule is one change per dataset: keep the shape (hash check) or rename to the game's words (`SchemaVersion` bump plus apps), never both. This change keeps the shape, so the unchanged manifest hash proves the migration. Renaming `salvage`/`components`/`forgeBadges` to the game's terms can be a later change.

### D2. Fail on any unmapped key

A key the reader does not map would mean the game added a cost the planner would silently drop. The reader throws `InvalidOperationException` (the loader's startup-failure contract) naming the dataset, the row and its keys.

### D3. Serve `level` from the row position

Unchanged from before: row N (0-based) is the cost to raise a MoW ability to level N + 2.
