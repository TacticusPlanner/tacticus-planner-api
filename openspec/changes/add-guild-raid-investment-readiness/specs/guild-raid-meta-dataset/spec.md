## MODIFIED Requirements

### Requirement: Boss Meta recommendations provide exact teams

Every element of `bosses` SHALL have `bossUnitSetId`, an ordered array
`primeUnitSetIds` of zero or more prime `raid-bosses` unit-set ids fought
alongside that boss, and an ordered, non-empty `recommendations` array. Each
recommendation SHALL have:

- `kind`: a non-empty archetype id, unique within the group it appears in
  (there is no fixed count or fixed set of allowed values — a boss/prime
  authors as many recommendations as its source documents);
- `heroIds`: an ordered array of exactly five distinct character ids;
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
- **THEN** each recommendation exposes exactly five ordered hero ids, one
  Machine of War id, one or more Comp ids, and a positive `efficiency` value

#### Scenario: A boss has several tiered recommendations

- **WHEN** a boss group contains three recommendations with distinct `kind`
  values
- **THEN** each recommendation exposes exactly five ordered hero ids, one
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
- `heroSlots`: an ordered array of exactly five slot objects, in the same order as `heroIds`;
- `mowReplacementIds`: an ordered array of zero or more distinct Machine-of-War ids.

Every hero-slot object SHALL have:

- `heroId`: the exact character id at the same array position in `heroIds`;
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

## ADDED Requirements

### Requirement: Primes provide their own curated recommendations

The served dataset SHALL have a `primes` array, ordered, of zero or more
groups. Every element SHALL have `primeUnitSetId` and a non-empty
`recommendations` array using exactly the same recommendation shape defined
for `bosses` (`kind`, `heroIds`, `mowId`, `compIds`, `efficiency`, `id`,
`heroSlots`, `mowReplacementIds`). A prime with no curated recommendation
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
