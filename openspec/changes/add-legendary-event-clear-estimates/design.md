# Design

## Context

- Catalog datasets flow raw embedded JSON → denormalization → served dataset with a content hash in the public manifest (`game-catalog-data` skill). Single-record datasets exist (`lre-common`, until the Stage 3 change removes it).
- `guild-raid-meta` recommendations carry a curated `efficiency` number validated as positive (`GuildRaidMetaValidation`, `InvalidEfficiency`); that is the precedent the Stage 1 follow-up named.
- The client computes combat power with `@workspace/game-domain` `characterCombatPower` (V1 port) and compares it to `lre-battles` `power`. Battle powers grow roughly geometrically (Uthar Alpha: 1,018; 3,055; 8,199; … 19,994,528 over 18 battles), so a single ratio between team power and battle power is the calibration knob.
- Combat power covers characters only (MoWs are not in LRE teams).

## Goals / Non-Goals

**Goals:**

- One small, curated record the client reads to tune estimates without a release.
- Safe defaults: an empty coefficient list and an uncalibrated ratio still produce a labelled estimate.

**Non-Goals:**

- Per-lane or per-event coefficients (one per unit for the mode; revisit if feedback shows lane-specific over-performance).
- Any server computation, aggregation or storage of estimates.

## Decisions

**D1. One record, three parts.** The dataset `lre-clear-estimate` serves exactly one record:

```
{
  id: "lre-clear-estimate",
  powerRatio: number,            // > 0 and <= 10; team effective power must reach powerRatio × battle power
  calibration: {
    sampleCount: int,            // >= 0; 0 = uncalibrated
    calibratedOn: string | null  // ISO 8601 date (yyyy-MM-dd); null when sampleCount is 0
  },
  unitCoefficients: { unitId: string, coefficient: number }[]   // ascending by unitId; coefficient > 0 and <= 5
}
```

The raw file has the same shape; denormalization only sorts `unitCoefficients` by `unitId` (ordinal) so the hash is stable regardless of authoring order. Rationale: ratio and coefficients are tuned together (raising a coefficient and lowering the ratio interact), so they version together under one hash; a single record matches how the client reads it.

**D2. Defaults are explicit, not implied by absence of the dataset.** A unit not listed has coefficient 1.0 (the client applies it; the server never expands the list). The initial raw file ships `unitCoefficients: []`, `calibration: { sampleCount: 0, calibratedOn: null }` and `powerRatio: 1.0` as a placeholder; the apps change's calibration task produces the first calibrated ratio and sample count, which land as a raw-file edit (no code change). The client labels estimates "uncalibrated" while `sampleCount` is 0.

**D3. Validation.** On load: exactly one record; `powerRatio` in (0, 10]; `sampleCount` ≥ 0; `calibratedOn` a valid date when `sampleCount` > 0 and null when 0; every `unitId` a catalog character id (not a MoW, not an NPC); `coefficient` in (0, 5]; no duplicate `unitId`. Failures are catalog load errors (`InvalidLreClearEstimate` with the offending value), like `GuildRaidMetaValidation`.

**D4. Curation.** Coefficients are hand-curated by maintainers from estimate-vs-actual evidence (the apps change logs overrides). The `game-catalog-data` skill gets a short section: what the numbers mean, the 1.0 baseline, the bounds, and that ratio and coefficient changes go together with a note of the evidence in the PR. Who owns curation long-term is open (docs feature spec open question).

**D5. Manifest.** The dataset is listed with its hash like every other; the manifest snapshot test changes. No change to `lres` or `lre-battles`.

## Risks / Trade-offs

- [Placeholder ratio 1.0 gives poor estimates before calibration] → the client shows "uncalibrated" and the user overrides; the calibration task is part of the apps change, with a deferral heading only if no samples can be collected in-session.
- [Curated numbers drift from game balance] → bounded values, evidence in PRs, and the override log to tune from.

## Migration Plan

None (catalog-only). Rollback removes the dataset; the client hides estimates when the dataset is absent.

## Open Questions

- Who curates the efficiency coefficients long-term? Default: maintainers, through catalog PRs.
