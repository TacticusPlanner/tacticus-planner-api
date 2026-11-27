## Why

The pinned datamine `items.json` holds 12 unreleased placeholder items (`I_Block_C005`, `I_Block_U005`, `I_Booster_Block_C001` to `L001`, `I_Defensive_C001` to `L001`). The game gives them no name: their config `name` is the id with a leading `_` (`_I_Block_C005`) and the language files have no `Items/<id>_name` term. The served `equipment` dataset carries them, so every client consumer has to hide them itself; V1's Equipment Lookup does it with three hand-written id patterns, and the V2 character page filters on the `_` prefix. The V2 Items page and Library Items collection would need the same filter again.

## What Changes

- An item whose config `name` starts with `_` (the game's marker for an item it has not named) is left out of the served `equipment` dataset. At datamine `v1.44.88.3089` that serves 205 of 217 items.
- Because characters' `eligibleEquipment` is built from the same item list, the placeholders also leave every character's eligible list. None of them is allowed to any faction or unit today, so no eligible list changes.
- The served shape is unchanged; only the `equipment` and `characters` dataset hashes can move.

Companion change: the V2 Items feature in `tacticus-planner-apps` (Items page and Library Items), which relies on the dataset holding released items only.

## Capabilities

### Modified Capabilities

- `equipment-dataset`: only items the game names are served.

## Impact

- `Denormalization/EquipmentDenormalizer.cs` and `EquipmentDenormalizerTests`.
- The public catalog manifest snapshot: the `equipment` dataset hash moves.
- No database change or migration. No endpoint or OpenAPI change.
