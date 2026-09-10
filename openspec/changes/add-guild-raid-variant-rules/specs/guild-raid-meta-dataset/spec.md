## ADDED Requirements

### Requirement: Exact recommendations expose explicit variant rules

Every recommendation SHALL additionally have:

- `id`: a non-empty recommendation id unique across the dataset;
- `heroSlots`: an ordered array of exactly five slot objects, in the same order as `heroIds`;
- `mowReplacementIds`: an ordered array of zero or more distinct Machine-of-War ids.

Every hero-slot object SHALL have:

- `heroId`: the exact character id at the same array position in `heroIds`;
- `roleId`: a non-empty stable role id interpreted by the client;
- `essential`: boolean indicating whether inability to fill this slot makes the recommendation unavailable;
- `replacementCharacterIds`: an ordered array of zero or more distinct character ids explicitly allowed for that slot.

The server SHALL preserve authored recommendation, slot, and replacement order. Comp core/flex/Machine-of-War lists remain broad advisory guidance and SHALL NOT be treated as implicit replacements. No effectiveness weight, investment target, damage estimate, strategy text, display name, icon path, or localized role label SHALL be present in the served projection.

#### Scenario: Recommendation supplies deterministic slot rules

- **WHEN** a client reads a recommendation with replacement guidance
- **THEN** it receives a stable id, five slots aligned to the exact heroes, ordered allowed character replacements per slot, and ordered allowed Machine-of-War replacements

#### Scenario: Slot intentionally has no replacement

- **WHEN** an authored hero slot has an empty `replacementCharacterIds` array
- **THEN** the empty list is served unchanged and no Comp member is inferred as a replacement

### Requirement: Variant-rule references and coverage are validated

Catalog loading SHALL reject Guild Raid Meta data when recommendation ids are duplicated, a recommendation does not have exactly five hero slots, a slot's `heroId` does not match the `heroIds` value at the same position, a replacement repeats its ideal hero, one replacement id is duplicated within a slot, or one Machine-of-War replacement repeats `mowId` or another replacement.

Every slot hero and replacement id SHALL resolve to a catalog character. Every Machine-of-War replacement id SHALL resolve to a catalog Machine of War. A character MAY be an allowed replacement in multiple slots or recommendations; uniqueness is enforced within each individual replacement list, not globally.

#### Scenario: Misaligned slot data fails catalog loading

- **WHEN** a recommendation's third hero slot names a different `heroId` than its third exact `heroIds` entry
- **THEN** catalog loading fails and the malformed dataset is not added to the manifest

#### Scenario: Unknown replacement fails catalog loading

- **WHEN** a replacement rule references an unknown character or Machine of War
- **THEN** catalog loading fails with the unresolved id identified

#### Scenario: Shared replacement is valid

- **WHEN** the same known character is explicitly allowed in different slots or recommendations
- **THEN** catalog loading accepts the reuse while preserving each list's authored order
