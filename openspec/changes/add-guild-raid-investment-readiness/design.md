## Context

See `proposal.md`. `Data/guild-raid-meta.json` currently holds `sourceId`,
`updatedOn`, `comps[]`, and `bosses[]` (each an authored group of exactly two
`meta`/`alternate` recommendations with `heroSlots` added by
`add-guild-raid-variant-rules`). That change's `heroSlots`/
`replacementCharacterIds`/`mowReplacementIds` content was explicitly authored
as an unreviewed draft (mechanically derived from each recommendation's own
referenced Comp) because no real source was available at the time; its task
1.2 ("review the authored data") was left incomplete.

Real data is now available from four sources (all fetched 2026-09-14):

- `terminusmaximus.com/guild-raid/boss-meta/` (the same site already cited by
  the catalog's `sourceId`) publishes, per boss, a named "Meta Team" and
  "Alternate Team" roster with replay-backed average/maximum damage figures,
  for all 12 bosses.
- `cognitae.app/meta` publishes 2–3 tiered archetypes per boss (Optimal/
  Strong/Viable, or similar) with named core/flex rosters and a Machine of
  War, pooled across guilds — but only for the 5 bosses currently in this
  season's Mythic/Legendary rotation (Lion, Silent King, Mortarion,
  Ghazghkull, Belisarius Cawl).
- `cognitae.app/side-bosses` publishes the same tiered-archetype shape for 14
  named primes across those same 5 bosses plus a few lower-tier ones, filling
  a gap no other source covered.
- `tacticus.wiki.gg/wiki/Guild_Raid` confirms each boss's two primes and
  faction restriction, cross-checked against `cognitae`'s prime names — they
  agree everywhere both cover the same prime.

`raid-bosses` already authors one JSON file per boss and merges them at load
time (`GameCatalogLoader.LoadRaidBossRawData`) into one served dataset — this
change applies the same authoring split to `guild-raid-meta` without changing
its served shape.

## Goals / Non-Goals

**Goals:**

- Replace placeholder recommendation rosters with sourced, cited data for all
  12 bosses, using every distinct archetype a source documents rather than
  forcing a fixed count.
- Add a sourced, per-recommendation relative-efficiency figure, consistent in
  meaning (weakest included recommendation for that boss/prime = `1.0`)
  regardless of which source backs a given boss.
- Add curated comps for primes where `cognitae.app/side-bosses` documents
  them.
- Split authoring into per-boss files without changing the served contract's
  shape (additive/widened fields only).

**Non-Goals:**

- Curated comps for a boss or prime neither source documents. Fabricating one
  would repeat the exact mistake this change is fixing.
- A server-computed player-specific readiness value. The threshold math the
  companion client change needs is derivable entirely from data already
  served (`raid-bosses` `StatProgression` + live `GuildRaidBossStatus`); no new
  API field is needed for it.
- An absolute damage-prediction or synergy model, or a cross-boss difficulty
  ranking. `efficiency` is relative within one boss/prime's own recommendations.

## Decisions

### Source the roster replacement outright, not a review pass

`add-guild-raid-variant-rules` task 1.2 asked for the placeholder to be
*reviewed*. Given real sources now exist, this change replaces the placeholder
rosters, roles, and replacement lists directly with sourced archetypes. This
satisfies 1.2 by making the review moot rather than performing it on data
about to be discarded.

### Widen `kind` from a fixed pair to a free-form archetype id, sized per boss to its sources

The prior schema fixed exactly two recommendations per boss (`kind: "meta"` /
`"alternate"`). `cognitae.app/meta` documents 2–3 distinct named archetypes
per boss it covers (e.g. Lion: Lavistodes, Neuro, Battlesuit), each with its
own roster and tier — forcing that down to two would discard sourced data.
`kind` becomes a non-empty string, unique within its boss/prime group, holding
the archetype's doctrine slug (`lavistodes`, `neuro`, `battlesuit`, `admech`,
`custodes`). A boss/prime authors as many recommendations as its best
available source documents:

- Where `cognitae` covers a boss (5 of 12): one recommendation per archetype
  it lists (2–3).
- Where only `terminusmaximus` covers a boss (7 of 12): exactly two
  (`meta`/`alternate`, kept as `kind` values since no richer archetype
  breakdown exists for them).

Validation requires at least one recommendation per boss group and unique
`kind` values within it; there is no fixed count and no upper bound enforced
in validation (four archetypes is the most any source currently shows, not a
hard ceiling).

### Two `efficiency` derivations, chosen per boss by which source covers it — never blended within one boss

Both derivations anchor the **weakest recommendation actually included for
that boss/prime at `1.0`**, and scale stronger ones above it, so the meaning
is consistent even though the inputs differ:

**Where `terminusmaximus` is the only source (7 bosses):**
`efficiency = round(recommendationAvgDamage / weakestAvgDamage, 2)`, from its
replay-backed average damage. Computed values:

| Boss | Meta efficiency | Alternate efficiency |
|---|---|---|
| Avatar of Khaine | 1.47 | 1.0 |
| Rogal Dorn | 1.17 | 1.0 |
| Hive Tyrant | 1.07 | 1.0 |
| Magnus | 1.06 | 1.0 |
| Riptide | 1.13 | 1.0 |
| Screamer-Killer | 1.33 | 1.0 |
| Szarekh | 1.44 | 1.0 |
| Tervigon | 1.30 | 1.0 |

(Lion, Silent King, Mortarion, Ghazghkull, Belisarius Cawl move to the
`cognitae`-derived table below instead, since `cognitae` covers them with
richer archetype data.)

**Where `cognitae` covers a boss/prime (5 bosses + 14 primes):** `cognitae`
publishes only qualitative tiers (Optimal/Excellent/Strong/Viable/Backup), not
exact numbers, but its own published rating-scale legend gives each tier a
floor percentage (Optimal = top team ≈ 100, Excellent ≥ 90, Strong ≥ 80,
Viable ≥ 70, Backup ≥ 60). `efficiency = round(tierFloor / weakestIncludedTierFloor, 2)`.
For Lion (Lavistodes=Optimal/100, Neuro=Strong/80, Battlesuit=Viable/70):
Battlesuit anchors at `1.0`, Neuro = `round(80/70, 2)` = `1.14`, Lavistodes =
`round(100/70, 2)` = `1.43`.

This intentionally does not force-reconcile against `terminusmaximus`'s exact
number for the same boss where both exist (e.g. Lion's Lavistodes/Neuro ratio
computes to `1.30` from replay damage vs. `1.25` from tier floors) — both are
real, sourced figures computed by different methods; the discrepancy is
expected and not corrected toward either one. Author from `cognitae`'s tiers
whenever `cognitae` covers the boss, since its richer archetype set is the
reason to prefer it there.

`efficiency` is a boss/prime-relative figure only, never a cross-boss
difficulty comparison. Validation requires `efficiency > 0` with no enforced
ceiling.

### Primes get their own `recommendations`, mirroring bosses

A new top-level `primes[]` array holds `{primeUnitSetId, recommendations[]}`,
reusing the exact recommendation shape `bosses[]` uses (`kind`, `heroIds`,
`heroSlots`, `mowId`, `mowReplacementIds`, `compIds`, `efficiency`). Sourced
from `cognitae.app/side-bosses` for the 14 primes it documents; a prime not
covered there gets no `primes[]` entry (the client's existing
roster-agnostic-primes fallback design applies), while its `unitSetId` still
appears in its boss's `primeUnitSetIds` for identification either way.

### Prime identification reuses the boss's own encounter data, not a new lookup

A boss's primes are its `Crystal`-type encounters' referenced `unitSetId`s —
the same relationship `add-raid-boss-mobile-picker` (apps repo, unimplemented)
already plans to derive client-side. This change adds the resolved
`primeUnitSetIds` directly to each boss's authored `guild-raid-meta` file,
confirmed against `tacticus.wiki.gg`/`cognitae` naming and matched to catalog
`unitSetId`s during implementation.

### Manifest and release semantics

Only the `guild-raid-meta` authoring path and its models/denormalizer/validator
change. The dataset hash and source hash change; every other dataset hash
stays stable. Widening `kind`'s allowed values and adding fields is not a
breaking served-shape change (the type is still a string; existing consumers
reading it as such are unaffected), so `SchemaVersion` stays unbumped under
the repository's breaking-shape policy; this is curated editorial content, so
`Version`/`GameVersion` are not bumped either.

### No persistence migration

Embedded catalog JSON only; no EF entity or database change.

## Risks / Trade-offs

- [Risk] `terminusmaximus`'s recurring unresolved roster slot (an Ork
  portrait, icon alt-text "Boss") is now corroborated as Gulgortz Ironskull —
  `cognitae` independently names "Gulgortz" in the equivalent Custodes/
  Lavistodes core for Mortarion, Silent King, and several primes, consistent
  with the portrait's description. → Treat as resolved for authoring, but
  spot-check the specific portrait against one source's media before
  finalizing, since this is inference from two sources agreeing, not either
  source stating the name outright.
- [Risk] Magnus's `terminusmaximus` figures (lowest of the 7 bosses on that
  table, Meta/Alternate nearly tied) may reflect a September 2026 kit rework
  rather than the current meta. → Author it as-is (it is still the best
  available source) and note the rework caveat in the commit/PR description.
- [Risk] Two bosses' `alternate` figures rest on a small replay sample (Hive
  Tyrant: 5, Tervigon: 4, vs. 10 for most others). → Author them as-is; the
  ratio is still the best available signal.
- [Risk] The two `efficiency` derivations are not on a strictly identical
  scale (replay-damage ratios vs. tier-floor ratios), so a value on a
  `cognitae`-sourced boss and a value on a `terminusmaximus`-sourced boss are
  even less comparable to each other than same-method values already are. →
  The spec explicitly forbids cross-boss comparison of `efficiency` regardless
  of source; document the dual methodology in the authored data's PR
  description.
- [Risk] Character display names from both sources must map to existing
  catalog character ids without inventing new ones. → Resolve every name
  against `Data/units/*.json` during implementation; any name that doesn't
  resolve is a blocker to surface, not a placeholder to invent.
- [Risk] `cognitae`'s current-season coverage (5 of 12 bosses) will shift as
  the rotation changes, so this authored snapshot will under-cover bosses that
  move into Mythic/Legendary later. → Out of scope for this change; a future
  content refresh re-checks `cognitae` coverage the same way `raid-bosses`
  data is periodically refreshed.

## Migration Plan

Apply and deploy the API catalog restructuring first. Review the manifest
snapshot to confirm that only `guild-raid-meta` and the aggregate source hash
changed. Then apply the companion client change. Rollback restores the prior
embedded JSON/models; no database state is involved.
