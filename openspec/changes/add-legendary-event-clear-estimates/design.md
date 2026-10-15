# Design

## Context

- Catalog datasets flow raw embedded JSON → denormalization → served dataset with a content hash in the public manifest (`game-catalog-data` skill). Single-record datasets exist (`lre-common`, until the Stage 3 change removes it).
- `guild-raid-meta` recommendations carry a curated `efficiency` number validated as positive (`GuildRaidMetaValidation`, `InvalidEfficiency`); that is the precedent the Stage 1 follow-up named for curated per-unit tuning.
- The client computes combat power with `@workspace/game-domain` `characterCombatPower` (V1 port) and compares it to `lre-battles` `power`. Battle powers grow roughly geometrically (Uthar Alpha: 1,018; 3,055; 8,199; … 19,994,528 over 18 battles), so a single ratio between team power and battle power is the calibration knob.
- Combat power covers characters only (MoWs are not in Legendary Event teams).
- Legendary Events are horde-style: many waves of weak enemies, where summoners, AoE / multi-hit and sustain outperform their raw power. That strength is a property of the character in horde modes generally, not of one event.

## Goals / Non-Goals

**Goals:**

- Small, curated records the client reads to tune estimates without a release.
- Per-character tuning that is easy to curate (a tier, not a number) and reusable by any horde-style mode.
- Safe defaults: an uncalibrated ratio and untiered characters still produce a labelled estimate.

**Non-Goals:**

- Per-lane or per-event tiers (one tier per character; revisit if feedback shows lane-specific over-performance).
- Any server computation, aggregation or storage of estimates.
- Renaming the pre-existing `lre-common`, `lres` and `lre-battles` datasets (shipped contracts; a separate change if wanted).

## Decisions

**D1. Clear-estimate record.** The dataset `legendary-event-clear-estimate` serves exactly one record, as-is from the raw file:

```
{
  id: "legendary-event-clear-estimate",
  powerRatio: number,            // > 0 and <= 10; team effective power must reach powerRatio × battle power
  calibration: {
    sampleCount: int,            // >= 0; 0 = uncalibrated
    calibratedOn: string | null  // ISO 8601 date (yyyy-MM-dd); null when sampleCount is 0
  }
}
```

**D2. Horde-mode tier list.** The dataset `horde-mode-tiers` serves exactly one record:

```
{
  id: "horde-mode-tiers",
  tiers: { tier: "S" | "A" | "B" | "C", multiplier: number }[],   // exactly S, A, B, C in that order; multiplier > 0 and <= 5
  characters: { unitId: string, tier: string }[]                  // ascending by unitId
}
```

Denormalization only sorts `characters` by `unitId` (ordinal) so the hash is stable regardless of authoring order, and serves a missing list as empty. The multipliers live in the data, not in the client, so re-weighting a tier is a catalog edit. A character not listed is untiered: multiplier 1.0, applied by the client (the server never expands the list). Rationale for a separate, generic dataset: the tier is a property of the character in horde-style modes, so other features can read it without depending on the Legendary Event estimate; a tier is easier to curate and review than a free-form coefficient.

Effective power for the estimate is `combat power × tier multiplier`. The ratio (D1) and the tier multipliers interact (raising a tier multiplier has the same effect as lowering the ratio for teams with that tier), so changes to either go in one PR with the evidence.

**D3. Defaults.** `legendary-event-clear-estimate` ships `powerRatio: 1.0` and `calibration: { sampleCount: 0, calibratedOn: null }` as a placeholder; the apps change's calibration task produces the first calibrated ratio and sample count, which land as a raw-file edit (no code change). The client labels estimates "uncalibrated" while `sampleCount` is 0. `horde-mode-tiers` ships placeholder multipliers S 1.3, A 1.2, B 1.1, C 0.9 and the review's seed list (S: Kariyan, Sho'syl, Gulgortz, Celestine; A: Aleph-Null, Archimatos, Anuphet; B: Angrax, Bellator), to be re-weighted from calibration evidence.

**D4. Validation.** On load, `legendary-event-clear-estimate`: exactly one record with the expected id; `powerRatio` in (0, 10]; `calibration` present; `sampleCount` ≥ 0; `calibratedOn` a valid date when `sampleCount` > 0 and null when 0 (`InvalidLegendaryEventClearEstimate`). `horde-mode-tiers`: exactly one record with the expected id; `tiers` exactly S, A, B, C in that order; each multiplier in (0, 5]; every `unitId` a catalog character id (not a MoW, not an NPC), listed once; every `tier` one of the tier names (`InvalidHordeModeTiers`). Failures are catalog load errors naming the offending value, like `GuildRaidMetaValidation`.

**D5. Curation.** The tier list and multipliers are hand-curated by maintainers from estimate-vs-actual evidence (the apps change logs overrides). The `game-catalog-data` skill gets a section for each dataset: what the numbers mean, the 1.0 baseline, the bounds, and that ratio and tier changes go together with a note of the evidence in the PR. Who owns curation long-term is open (docs feature spec open question).

**D6. Manifest.** Both datasets are listed with their hashes like every other; the manifest snapshot test changes. No change to `lres` or `lre-battles`.

## Risks / Trade-offs

- [Placeholder ratio and multipliers give poor estimates before calibration] → the client shows "uncalibrated" and the user overrides; the calibration task is part of the apps change, with a deferral heading only if no samples can be collected in-session.
- [Four tiers are coarser than a free coefficient] → the multipliers are data and can be re-weighted; a finer scale is a later schema change if evidence needs it.
- [Seed tier list is unverified community opinion] → it is a starting point, curated through catalog PRs like any other value.

## Migration Plan

None (catalog-only). Rollback removes the datasets; the client hides estimates when `legendary-event-clear-estimate` is absent and treats a missing `horde-mode-tiers` as all-untiered.

## Open Questions

- Who curates the tier list long-term? Default: maintainers, through catalog PRs.
