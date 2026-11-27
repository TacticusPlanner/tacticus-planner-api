## Context

`GameCatalogDenormalizer.ReadDatamineEquipment` reads the datamine `gameconfig/items.json` into catalog equipment grouped by type; both the served `equipment` view and each character's `eligibleEquipment` come from that grouping. The game's language files are not embedded in the API, so the only in-config marker of an unnamed item is its config `name`, which the game sets to `_` plus the id for every placeholder.

## Decisions

- **Filter at read time.** The placeholders are dropped in `ReadDatamineEquipment`, before grouping, so the served view and the eligible lists agree and no later step sees them.
- **Marker: a config name starting with `_`.** It matches all 12 placeholders and no named item at `v1.44.88.3089`, needs no language file, and is the rule the V2 character page already uses on the client. An item the game later names gets a real config name and returns on the next datamine bump.
- **Raw data, served shape and manifest.** No raw file changes. The served projection keeps every field. The manifest snapshot moves only the `equipment` dataset hash (the `characters` hash stays, since no placeholder is in an eligible list).

## Risks

- A player whose synced data held a placeholder id would find no catalog record for it. The client already treats an unknown item id as "Unknown item" rather than failing.
