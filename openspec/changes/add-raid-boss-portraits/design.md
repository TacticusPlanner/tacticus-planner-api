## Context

See proposal.md — Why. `QuestUnitId` already exists on
`GameCatalogRaidBossRawUnitSet` (parsed from the per-boss raw files under
`Data/raid-bosses/`), it is simply not carried into the served
`GameCatalogRaidBossView`. Every other optional per-unit field on that view
(`Weapons`, `*AbilityIds`, `TraitIds`) uses
`JsonIgnore(Condition = WhenWritingNull)`.

## Goals / Non-Goals

**Goals:**

- Thread one existing raw field into the served projection with no behavior
  change for any other field.
- Keep the raw-vs-served split reviewable: the served value is a verbatim copy,
  no transformation.

**Non-Goals:**

- No portrait/icon path resolution on the server (client concern — see the
  companion apps change).
- No new validation. `questUnitId` is advisory metadata for the client's
  portrait lookup; its absence or an unresolvable value is not a build error.
- No change to encounter `fieldNpcIds`, which stay as-is.

## Decisions

- **Add `QuestUnitId` as the last optional field on `GameCatalogRaidBossView`**,
  mapped in `RaidBossDenormalizer` from `rawUnitSet.QuestUnitId`. Rationale:
  mirrors how the denormalizer already forwards the other optional collections;
  appending keeps the positional record layout stable for readers.
  Alternative considered — a separate `raid-boss-quest-units` sub-dataset:
  rejected, the value is 1:1 with a unit set already in the payload, so a
  sibling file would just force a client-side join the projection exists to
  avoid.
- **Contract surface shared with the companion apps change:** the served
  `raid-bosses` dataset payload (the `questUnitId` field on each boss/prime
  record). No endpoint signature change.

## Risks / Trade-offs

- [Served `raid-bosses` hash changes → every client re-downloads the dataset on
  next manifest diff] → Expected and cheap; the manifest-diff sync is built for
  exactly this. Regenerate the manifest snapshot in the same change so CI
  stays green.
- [`questUnitId` points at an npc id the client has no portrait asset for] →
  Client already degrades a missing portrait asset to a text badge
  (`raid-boss-catalog` icon requirement); a stale or unknown id is harmless.

## Migration Plan

1. Add the field + denormalizer mapping.
2. Run the catalog tests; update the manifest snapshot `.verified.txt`.
3. Merge before the companion apps change (API applies first).
   Rollback: revert the field; the client treats `questUnitId` as optional and
   falls back to its fuzzy name match, so an older payload keeps working.
