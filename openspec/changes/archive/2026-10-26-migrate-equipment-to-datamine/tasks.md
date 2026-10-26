# Tasks

Companion apps change: none (served shape unchanged). Source plan: `tacticus-planner-data-mines` `docs/roadmap.md` step 5 and `docs/catalog-audit.md`.

## 1. Binding

- [x] 1.1 Add `DatamineItem` / `DatamineItemLevel` and move the upgrade ladder onto `GameCatalogEquipment.UpgradeLevels`
- [x] 1.2 Rewrite `EquipmentDenormalizer` to read `gameconfig/items.json` (stat renames, per-level costs, defaults, type-then-id order)
- [x] 1.3 Load it through `DatamineFiles.Open` in `GameCatalogLoader`; drop `EquipmentTypes` / `EquipmentUpgradeCosts` from `GameCatalogDatasets` and the snapshot
- [x] 1.4 Delete `Data/equipment/**` and `Data/equipment-upgrade-costs.json`

## 2. Allowlist

- [x] 2.1 Remove `gameconfig/globalValues.json` from `files.txt` and re-run `pull-datamine.cs -- v1.43.157.2989` (prunes the copy, rewrites `files.sha256`, `REF` unchanged)

## 3. Verification

- [x] 3.1 Dump the served `equipment` view on `main` and on this branch: same 217 objects, equal once sorted by (type, id)
- [x] 3.2 Add `EquipmentDenormalizerTests` (stat renames, ladder and defaults, order, served stat names, one ladder per rarity)
- [x] 3.3 Promote the manifest snapshot: only the `equipment` hash moves
- [x] 3.4 Format, build, all test projects

## 4. Docs

- [x] 4.1 Update the `game-catalog-data` skill (both mirrors): equipment source and mapping, registry, migrated list
Follow-up after merge: mark roadmap step 5 done in `tacticus-planner-data-mines`.
