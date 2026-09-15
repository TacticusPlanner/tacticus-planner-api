## Why

The Guild Raid Meta catalog is one authored file, and its `heroSlots`/replacement
data is still the unreviewed placeholder drafted in `add-guild-raid-variant-rules`
(derived mechanically from Comp membership, never checked against a real source).
Separately, exact readiness only reports owned/missing per hero — it cannot say
whether an *owned* hero is actually leveled enough for the boss the guild is
currently fighting, and there is no signal at all for how much better one
authored recommendation performs than another, or for the primes fought
alongside each boss at all. Real community data now exists to fix all three:
`terminusmaximus.com` (the source already cited as `sourceId`) gives replay-backed
average-damage figures for two archetypes per boss; `cognitae.app/meta` and
`cognitae.app/side-bosses` give 2–4 tiered archetypes (Optimal/Excellent/Strong/
Viable/Backup) per boss *and* per prime, pooled across guilds; `tacticus.wiki.gg`
cross-checks boss/prime identity.

## What Changes

- Replace the placeholder `heroSlots`/`mowReplacementIds` authored in
  `add-guild-raid-variant-rules` with real sourced team rosters for all 12
  bosses: every distinct tiered archetype `cognitae.app/meta` documents for a
  boss (2–4, where covered), or `terminusmaximus.com`'s Meta/Alternate pair
  where `cognitae` has no data for that boss this season.
- Widen `kind` from a closed `meta`/`alternate` enum to a non-empty archetype
  id unique within its boss/prime group (e.g. `lavistodes`, `neuro`,
  `battlesuit`, `admech`), since a boss may now carry more than two authored
  recommendations.
- Add a recommendation-level `efficiency` figure, anchored per boss/prime so
  its weakest included recommendation is `1.0` and stronger ones exceed it —
  see the design doc for the two derivations (real replay-damage ratio where
  `terminusmaximus` covers a boss; `cognitae`'s own published tier-floor
  percentages — 100/90/80/70/60 for Optimal/Excellent/Strong/Viable/Backup —
  where only tier labels exist).
- Add a `primes[]` array, parallel to `bosses[]`, carrying the same
  recommendation shape keyed by prime `unitSetId` — curated comps for primes
  are now sourced (from `cognitae.app/side-bosses`) where this change previously
  found none. Each boss's `primeUnitSetIds` links to these.
- Split the single `Data/guild-raid-meta.json` authoring file into one
  `guild-raid-comps.json` (source id, update date, Comp profiles — unchanged
  shape) plus one file per boss under `Data/guild-raid-meta/`, mirroring the
  `raid-bosses` per-file authoring convention. This is authoring-only: the
  loader still merges everything into one served `GameCatalogGuildRaidMetaView`,
  exactly as `raid-bosses` merges its per-file sources today.
- Coordinate the matching `add-guild-raid-investment-readiness` change in
  `tacticus-planner-apps`; this API side applies first.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `guild-raid-meta-dataset`: Replaces placeholder recommendation rosters with
  sourced data across a variable number of recommendations per boss (was
  fixed at exactly two), adds a per-recommendation `efficiency` figure, adds
  a `primes[]` array of curated prime comps, and widens `kind` to a free-form
  archetype id. The served payload shape is additive/widened; no served field
  is removed.

## Impact

- Affects the authored `Data/guild-raid-meta.json` (replaced by
  `Data/guild-raid-meta/*.json`), raw/served models, the loader's merge step,
  denormalization, validation, manifest hash/snapshot, and game-catalog tests.
- Changes the catalog payload's recommendation content, widens `kind`, and
  adds `efficiency`/`primes[]`; the client schema in `tacticus-planner-apps`
  gains the same in the companion change.
- Closes out `add-guild-raid-variant-rules` task 1.2 (the deferred review of
  placeholder data) by replacing that data outright rather than merely
  reviewing it.
- Does not add damage prediction or synergy scoring beyond the sourced
  `efficiency` figure; does not add curated comps for a boss/prime neither
  source documents (7 of 12 bosses currently have no `cognitae` coverage and
  keep only their `terminusmaximus` meta/alternate pair).
