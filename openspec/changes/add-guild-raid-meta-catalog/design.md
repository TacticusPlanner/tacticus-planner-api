## Context

See `proposal.md` for motivation and `specs/guild-raid-meta-dataset/spec.md`
for the served contract. The existing `raid-bosses` catalog dataset is raw
game-data-derived and already provides boss/prime/season records; V1's Comp
taxonomy is curated strategy data. The companion apps change with the same
name needs a manifest-synced, id-only contract before the Library can render
Meta guidance.

## Goals / Non-Goals

**Goals:**

- Publish one validated, independently hashable curated Meta dataset through
  the existing anonymous game-catalog pipeline.
- Keep all team, Comp, and evidence references stable and presentation-free.
- Fail fast on stale or invalid relationships to boss, character, and Machine
  of War catalog data.

**Non-Goals:**

- No database storage, editorial UI, replay ingestion, ranking algorithm, or
  automatic collection from external sites.
- No change to the existing `raid-bosses` payload, its season calculations, or
  its hash.
- No API-specific display labels, portrait paths, or outbound source URLs.

## Decisions

### 1. Publish `guild-raid-meta` as a separate game-catalog dataset

Add one embedded raw JSON source and one served dataset key, modelled and
denormalized alongside the existing catalog datasets. It receives its own
manifest hash and anonymous endpoint. This lets editorial updates sync
independently and keeps the game-derived `raid-bosses` contract stable.

Alternative rejected: append recommendations to `raid-bosses`. That couples a
frequently revised strategy source to encounter data and forces clients to
re-download unchanged game data.

### 2. Keep one canonical id-only projection

The raw source and served view use the contract in the spec: source/update
metadata, Comp profiles, and boss recommendation groups. A small dedicated
denormalizer copies ordered records into immutable served views; it does not
join or duplicate character, MoW, boss, names, icons, or URLs. Cross-reference
validation runs over the raw data and the loaded character/MoW/raid-boss
snapshot before a payload is exposed.

The `sourceId` is deliberately an opaque stable id; the companion apps change
maps it to localized attribution and an outbound URL. `updatedOn` is a data
revision date, not an in-game version or a recurrence schedule.

### 3. Validate authored strategy data as strictly as game data

Extend the catalog snapshot, loader, validator, manifest validation, and
endpoint registration for the new dataset. Validate list cardinality and
uniqueness locally, then resolve references against characters, MoWs, and
boss-only raid-boss records. Content updates remain additive at the catalog
schema level, so `SchemaVersion` remains unchanged.

Alternative rejected: client-only validation. It would allow a bad curation
file to reach every browser and create broken portraits or team cards.

### 4. No persistence migration is required

The source is embedded catalog JSON, not EF-backed product data. Adding the
served dataset changes the public catalog contract and generated OpenAPI only;
there is no EF Core migration, data backfill, or database rollback step.

## Risks / Trade-offs

- [Curated advice ages faster than game data] -> `updatedOn` and independently
  hashed data make the revision visible and cheap to refresh; no claim of live
  replay analysis is made.
- [A game-data rename/removal invalidates guidance] -> startup validation fails
  before serving inconsistent data, requiring the curated source to be updated
  with the same release.
- [A single external source changes its URL or methodology] -> preserve only a
  stable source id in the API; client attribution can evolve without an API
  contract change.

## Migration Plan

1. Add and validate the embedded dataset, then build and run catalog/API tests.
2. Review the manifest snapshot: the only new dataset hash is
   `guild-raid-meta` (with expected source-hash change).
3. Deploy the API change before the companion apps change. Older clients ignore
   the extra manifest entry; updated clients synchronize it.
4. Roll back by deploying the preceding API version; no persisted migration or
   data cleanup is required.
