# guild-raid-meta-dataset Specification

## Purpose

Provides an anonymous, versioned catalog of curated Guild Raid Boss team
recommendations and reusable Comp guidance that clients can render entirely
from stable game-data ids.

## Requirements

### Requirement: The catalog serves an id-only Guild Raid Meta dataset

The game catalog SHALL serve one non-empty `guild-raid-meta` dataset through
the public manifest and the anonymous `game-catalog/guild-raid-meta` endpoint.
The served payload SHALL be an object with these fields:

- `sourceId`: non-empty string identifying the editorial source;
- `updatedOn`: ISO-8601 calendar date string for the curated data revision;
- `comps`: ordered array of Comp profiles;
- `bosses`: ordered array of per-boss recommendation groups.

The payload SHALL participate in manifest hash comparison independently of
`raid-bosses`. Updating only curated Meta data SHALL change the
`guild-raid-meta` dataset hash and source hash, without changing the
`raid-bosses` dataset hash. Content-only updates do not require a catalog
schema-version bump; a breaking served-field removal does.

The server SHALL send no display names, translated copy, icon paths, image
asset identifiers, or source URLs. The client SHALL derive those from the
stable ids and its own presentation resources.

#### Scenario: A client discovers and downloads Meta data

- **WHEN** a client reads a manifest that includes a changed `guild-raid-meta`
  hash
- **THEN** it can anonymously download a non-empty payload matching the
  declared structure from `game-catalog/guild-raid-meta`

#### Scenario: An editorial update is isolated from game encounter data

- **WHEN** only a recommendation, Comp profile, source id, or
  update date changes
- **THEN** the new manifest changes the `guild-raid-meta` hash but leaves the
  `raid-bosses` hash unchanged

### Requirement: Comp profiles define reusable team-building guidance

Every element of `comps` SHALL have this structural shape:

- `id`: a non-empty, unique Comp id;
- `signatureUnitId`: a character or Machine of War id used by the client to
  identify the Comp;
- `coreCharacterIds`: ordered array of zero or more character ids;
- `flexCharacterIds`: ordered array of zero or more character ids;
- `mowIds`: ordered array of zero or more Machine of War ids.

All listed ids are advisory composition guidance. A unit may appear in more
than one Comp and in both core/flex lists of different Comps. The server SHALL
preserve the authored Comp and member ordering.

#### Scenario: A Comp can guide a team build

- **WHEN** a client reads a Comp profile
- **THEN** it receives its stable signature, ordered core-character,
  flex-character, and Machine-of-War ids without any presentation fields

### Requirement: Boss Meta recommendations provide exact teams

Every element of `bosses` SHALL have `bossUnitSetId` and an ordered,
non-empty `recommendations` array. Each recommendation SHALL have:

- `kind`: exactly `meta` or `alternate`;
- `heroIds`: an ordered array of exactly five distinct character ids;
- `mowId`: one Machine of War id;
- `compIds`: an ordered, non-empty array of unique Comp ids.

Within one boss group, recommendation kinds SHALL be unique. A boss may have
no group when no curated recommendation has been published; its absence is a
valid selection state, distinct from an absent or malformed dataset. The
server SHALL preserve authored boss, recommendation, hero, and Comp ordering.

#### Scenario: A boss has Meta and alternate exact teams

- **WHEN** a boss group contains a `meta` and an `alternate` recommendation
- **THEN** each recommendation exposes exactly five ordered hero ids, one
  Machine of War id, and one or more Comp ids

#### Scenario: A boss has no curated recommendation

- **WHEN** a served raid-boss unit set has no group in `bosses`
- **THEN** the `guild-raid-meta` dataset remains valid and a client can
  distinguish that no Meta information is available for that boss from a
  missing dataset

### Requirement: Guild Raid Meta references are validated at catalog load

Catalog loading SHALL reject malformed Guild Raid Meta source data before the
manifest or endpoint is available. Validation SHALL require non-empty source,
Comp, and recommendation collections; unique Comp ids and boss-unit-set ids;
unique recommendation kinds within each boss group; and the structural
constraints described above.

Every `bossUnitSetId` SHALL resolve to a served `raid-bosses` record whose
kind is `boss`. Every hero, core, and flex id SHALL resolve to a catalog
character; every `mowId` and applicable `signatureUnitId` SHALL resolve to a
catalog Machine of War; a character signature SHALL resolve to a catalog
character; and every recommendation `compId` SHALL resolve to an authored
Comp profile.

#### Scenario: Invalid cross-references fail fast

- **WHEN** a recommendation references an unknown hero, Machine of War, Comp,
  or a prime/nonexistent boss unit-set id
- **THEN** catalog loading fails with a validation error and the malformed
  dataset is not served

#### Scenario: A valid shared unit is reusable

- **WHEN** the same character or Machine of War is referenced by multiple
  Comp profiles or recommendations
- **THEN** catalog loading accepts the data when every reference resolves and
  each individual list satisfies its uniqueness rules

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

Catalog loading SHALL reject Guild Raid Meta data when a recommendation id is empty or duplicated, a recommendation does not have exactly five hero slots, a slot's `roleId` is empty, a slot's `heroId` does not match the `heroIds` value at the same position, a replacement repeats its ideal hero, one replacement id is duplicated within a slot, or one Machine-of-War replacement repeats `mowId` or another replacement.

Every slot hero and replacement id SHALL resolve to a catalog character. Every Machine-of-War replacement id SHALL resolve to a catalog Machine of War. A character MAY be an allowed replacement in multiple slots or recommendations; uniqueness is enforced within each individual replacement list, not globally.

#### Scenario: Misaligned slot data fails catalog loading

- **WHEN** a recommendation's third hero slot names a different `heroId` than its third exact `heroIds` entry
- **THEN** catalog loading fails and the malformed dataset is not added to the manifest

#### Scenario: Unknown replacement fails catalog loading

- **WHEN** a replacement rule references an unknown character or Machine of War
- **THEN** catalog loading fails with the unresolved id identified

#### Scenario: Required rule identity is empty

- **WHEN** a recommendation has an empty `id` or any hero slot has an empty `roleId`
- **THEN** catalog loading fails with the empty field identified

#### Scenario: Shared replacement is valid

- **WHEN** the same known character is explicitly allowed in different slots or recommendations
- **THEN** catalog loading accepts the reuse while preserving each list's authored order
