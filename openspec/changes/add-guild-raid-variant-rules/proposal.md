## Why

The existing Guild Raid Meta catalog contains exact teams and broad Comp pools, but those pools are not safe substitution rules: some exact recommendations intentionally fall outside their referenced Comp lists. Playable roster variants therefore need explicit, authored replacement relationships rather than client inference.

## What Changes

- Extend each exact recommendation with a stable recommendation id and five explicit hero-slot rules.
- Give each slot a role id, an essential flag, and an ordered list of allowed replacement character ids.
- Add an ordered list of allowed Machine-of-War replacements for each recommendation.
- Validate identity, coverage, uniqueness, catalog references, and deterministic authored ordering without adding effectiveness weights or performance scores.
- Preserve the id-only served projection; names, portraits, localized role labels, and explanatory copy remain client responsibilities.
- Coordinate the matching `add-guild-raid-variant-rules` change in `tacticus-planner-apps`; the API side applies first.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guild-raid-meta-dataset`: Adds explicit recommendation identity and slot-level character/Machine-of-War replacement rules to the served catalog contract.

## Impact

- Affects the authored `guild-raid-meta` data, raw and served models, denormalization, validation, manifest hash/snapshot, and game-catalog tests.
- Changes the catalog payload consumed by `tacticus-planner-apps`; the client schema and local catalog sync must be updated in the companion change.
- Does not add strategy notes, investment targets, effectiveness estimates, or advanced scoring.
