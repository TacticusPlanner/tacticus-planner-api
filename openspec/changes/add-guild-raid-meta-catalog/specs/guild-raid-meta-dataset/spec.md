## Purpose

Provides an anonymous, versioned catalog of curated Guild Raid Boss team
recommendations and reusable Comp guidance that clients can render entirely
from stable game-data ids.

## ADDED Requirements

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
`raid-bosses` dataset hash or requiring a catalog schema-version bump.

The server SHALL send no display names, translated copy, icon paths, image
asset identifiers, or source URLs. The client SHALL derive those from the
stable ids and its own presentation resources.

#### Scenario: A client discovers and downloads Meta data

- **WHEN** a client reads a manifest that includes a changed `guild-raid-meta`
  hash
- **THEN** it can anonymously download a non-empty payload matching the
  declared structure from `game-catalog/guild-raid-meta`

#### Scenario: An editorial update is isolated from game encounter data

- **WHEN** only a recommendation, Comp profile, evidence value, source id, or
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

### Requirement: Boss Meta recommendations provide exact teams and evidence

Every element of `bosses` SHALL have `bossUnitSetId` and an ordered,
non-empty `recommendations` array. Each recommendation SHALL have:

- `kind`: exactly `meta` or `alternate`;
- `heroIds`: an ordered array of exactly five distinct character ids;
- `mowId`: one Machine of War id;
- `compIds`: an ordered, non-empty array of unique Comp ids;
- `evidence`: either omitted or an object containing non-negative integer
  `replayCount`, `averageDamage`, and `maximumDamage` values.

Within one boss group, recommendation kinds SHALL be unique. A boss may have
no group when no curated recommendation has been published; its absence is a
valid selection state, distinct from an absent or malformed dataset. The
server SHALL preserve authored boss, recommendation, hero, and Comp ordering.

#### Scenario: A boss has Meta and alternate exact teams

- **WHEN** a boss group contains a `meta` and an `alternate` recommendation
- **THEN** each recommendation exposes exactly five ordered hero ids, one
  Machine of War id, one or more Comp ids, and its own evidence when authored

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
