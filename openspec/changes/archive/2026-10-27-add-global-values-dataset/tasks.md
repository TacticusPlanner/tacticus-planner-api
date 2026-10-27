# Tasks

Companion apps change: roadmap step 7 "Use `global-values`" (after all step 7 API PRs). Source plan:
`tacticus-planner-data-mines` `docs/roadmap.md` step 7.

## 1. Binding

- [x] 1.1 Add `gameconfig/globalValues.json` and `gameconfig/shop/staminaShopTiers.json` to `files.txt` and run `pull-datamine.cs -- v1.43.157.2989`
- [x] 1.2 Add `ReadDatamineGlobalValues` (values, trait variables, shop ladder through `products.json`) and load `products.json` once for it and `lres`
- [x] 1.3 Register `global-values` as a served dataset, add its endpoint, bump `SchemaVersion` to 12

## 2. Verification

- [x] 2.1 Add `GlobalValuesDenormalizerTests` (shipped values, defaults, missing and out-of-range values, unmappable tiers)
- [x] 2.2 Manifest snapshot: one new dataset, `schemaVersion` 12, no other hash moves
- [x] 2.3 Format, build, all test projects

## 3. Docs

- [x] 3.1 Update the `game-catalog-data` skill (both mirrors)
