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
- `bosses`: ordered array of per-boss recommendation groups;
- `primes`: ordered array of per-prime recommendation groups (see "Primes
  provide their own curated recommendations" below).

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

Every element of `bosses` SHALL have `bossUnitSetId`, an ordered array
`primeUnitSetIds` of zero or more prime `raid-bosses` unit-set ids fought
alongside that boss, and an ordered, non-empty `recommendations` array. Each
recommendation SHALL have:

- `kind`: a non-empty archetype id, unique within the group it appears in
  (there is no fixed count or fixed set of allowed values — a boss/prime
  authors as many recommendations as its source documents);
- `heroSlots`: an ordered array of exactly five slot objects, each naming a
  distinct character id (see below — this is the sole source of exact-team
  hero identity; there is no separate flat hero-id array);
- `mowId`: one Machine of War id;
- `compIds`: an ordered, non-empty array of unique Comp ids;
- `efficiency`: a positive number expressing that recommendation's relative
  performance against the other recommendation(s) authored for the same
  boss/prime.

Within one boss group, recommendation `kind` values SHALL be unique. A boss
may have no group when no curated recommendation has been published; its
absence is a valid selection state, distinct from an absent or malformed
dataset. The server SHALL preserve authored boss, recommendation, hero, and
Comp ordering.

`efficiency` is relative within its own boss/prime group only: it SHALL NOT
be compared across different bosses or primes to imply one is harder or
easier than another. It carries no unit and no fixed baseline value.

#### Scenario: A boss has Meta and alternate exact teams

- **WHEN** a boss group contains two recommendations, `kind: "meta"` and
  `kind: "alternate"`
- **THEN** each recommendation exposes exactly five ordered hero slots, one
  Machine of War id, one or more Comp ids, and a positive `efficiency` value

#### Scenario: A boss has several tiered recommendations

- **WHEN** a boss group contains three recommendations with distinct `kind`
  values
- **THEN** each recommendation exposes exactly five ordered hero slots, one
  Machine of War id, one or more Comp ids, and a positive `efficiency` value,
  in authored order

#### Scenario: A boss has no curated recommendation

- **WHEN** a served raid-boss unit set has no group in `bosses`
- **THEN** the `guild-raid-meta` dataset remains valid and a client can
  distinguish that no Meta information is available for that boss from a
  missing dataset

#### Scenario: A boss's primes are identified

- **WHEN** a client reads a boss group with a non-empty `primeUnitSetIds`
- **THEN** it receives each prime's `raid-bosses` unit-set id, in authored
  order

### Requirement: Guild Raid Meta references are validated at catalog load

Catalog loading SHALL reject malformed Guild Raid Meta source data before the
manifest or endpoint is available. Validation SHALL require non-empty source
and Comp collections; unique Comp ids and boss/prime-unit-set ids; unique
recommendation `kind` values within each boss/prime group; a positive
`efficiency` value on every recommendation; and the structural constraints
described in the other requirements of this capability.

Every `bossUnitSetId` and `primes[].primeUnitSetId` SHALL resolve to a served
`raid-bosses` record of the matching kind (`boss` or `prime`). Every
`primeUnitSetIds` entry on a boss group SHALL resolve to a served
`raid-bosses` prime record. Every hero, core, and flex id SHALL resolve to a
catalog character; every `mowId` and applicable `signatureUnitId` SHALL
resolve to a catalog Machine of War; a character signature SHALL resolve to a
catalog character; and every recommendation `compId` SHALL resolve to an
authored Comp profile.

#### Scenario: Invalid cross-references fail fast

- **WHEN** a recommendation references an unknown hero, Machine of War, Comp,
  or a prime/nonexistent boss unit-set id, or a boss group references a prime
  unit-set id that does not resolve to a `raid-bosses` prime
- **THEN** catalog loading fails with a validation error and the malformed
  dataset is not served

#### Scenario: Non-positive efficiency fails catalog loading

- **WHEN** a recommendation's `efficiency` is zero or negative
- **THEN** catalog loading fails with the invalid value identified

#### Scenario: Duplicate archetype kind fails catalog loading

- **WHEN** two recommendations in the same boss or prime group share the same
  `kind` value
- **THEN** catalog loading fails with the duplicate identified

#### Scenario: A valid shared unit is reusable

- **WHEN** the same character or Machine of War is referenced by multiple
  Comp profiles or recommendations
- **THEN** catalog loading accepts the data when every reference resolves and
  each individual list satisfies its uniqueness rules

### Requirement: Exact recommendations expose explicit variant rules

Every recommendation SHALL additionally have:

- `id`: a non-empty recommendation id unique across the dataset;
- `mowReplacementIds`: an ordered array of zero or more distinct Machine-of-War ids.

Every hero-slot object (in the `heroSlots` array defined above) SHALL have:

- `heroId`: the exact character id for this slot;
- `roleId`: a non-empty stable role id interpreted by the client;
- `essential`: boolean indicating whether inability to fill this slot makes the recommendation unavailable;
- `replacementCharacterIds`: an ordered array of zero or more distinct character ids explicitly allowed for that slot.

The server SHALL preserve authored recommendation, slot, and replacement
order. Comp core/flex/Machine-of-War lists remain broad advisory guidance and
SHALL NOT be treated as implicit replacements. No investment target, damage
estimate, strategy text, display name, icon path, or localized role label
SHALL be present in the served projection; the recommendation-level
`efficiency` figure defined above is the one deliberate exception to this
dataset's prior no-effectiveness-weight constraint.

#### Scenario: Recommendation supplies deterministic slot rules

- **WHEN** a client reads a recommendation with replacement guidance
- **THEN** it receives a stable id, five slots aligned to the exact heroes, ordered allowed character replacements per slot, and ordered allowed Machine-of-War replacements

#### Scenario: Slot intentionally has no replacement

- **WHEN** an authored hero slot has an empty `replacementCharacterIds` array
- **THEN** the empty list is served unchanged and no Comp member is inferred as a replacement

### Requirement: Variant-rule references and coverage are validated

Catalog loading SHALL reject Guild Raid Meta data when a recommendation id is
empty or duplicated, a recommendation does not have exactly five hero slots,
two of a recommendation's hero slots name the same `heroId`, a slot's
`roleId` is empty, a replacement repeats its ideal hero, one replacement id
is duplicated within a slot, or one Machine-of-War replacement repeats
`mowId` or another replacement.

Every slot hero and replacement id SHALL resolve to a catalog character.
Every Machine-of-War replacement id SHALL resolve to a catalog Machine of
War. A character MAY be an allowed replacement in multiple slots or
recommendations; uniqueness is enforced within each individual replacement
list, not globally.

#### Scenario: Duplicate hero across slots fails catalog loading

- **WHEN** a recommendation's five hero slots name the same character in two
  different slots
- **THEN** catalog loading fails and the malformed dataset is not added to
  the manifest

#### Scenario: Unknown replacement fails catalog loading

- **WHEN** a replacement rule references an unknown character or Machine of
  War
- **THEN** catalog loading fails with the unresolved id identified

#### Scenario: Required rule identity is empty

- **WHEN** a recommendation has an empty `id` or any hero slot has an empty
  `roleId`
- **THEN** catalog loading fails with the empty field identified

#### Scenario: Shared replacement is valid

- **WHEN** the same known character is explicitly allowed in different slots
  or recommendations
- **THEN** catalog loading accepts the reuse while preserving each list's
  authored order

### Requirement: Primes provide their own curated recommendations

The served dataset SHALL have a `primes` array, ordered, of zero or more
groups. Every element SHALL have `primeUnitSetId` and a non-empty
`recommendations` array using exactly the same recommendation shape defined
for `bosses` (`kind`, `heroSlots`, `mowId`, `compIds`, `efficiency`, `id`,
`mowReplacementIds`). A prime with no curated recommendation
SHALL simply have no entry in `primes` — this is a valid state, not an error,
and is distinct from that prime being unknown to the catalog entirely (it can
still appear in a boss's `primeUnitSetIds`).

`efficiency` within a prime's recommendations follows the same boss-relative
(here, prime-relative) rule as `bosses[].recommendations[].efficiency`.

#### Scenario: A prime has curated recommendations

- **WHEN** a client reads a `primes` entry
- **THEN** it receives that prime's unit-set id and one or more
  recommendations in authored order, each with the full recommendation shape

#### Scenario: A prime has no curated recommendation

- **WHEN** a prime referenced by a boss's `primeUnitSetIds` has no entry in
  `primes`
- **THEN** the dataset remains valid and a client can distinguish "no curated
  comp for this prime" from a missing dataset
