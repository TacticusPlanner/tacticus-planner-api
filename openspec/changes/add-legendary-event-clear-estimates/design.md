# Design

## Context

- Catalog datasets flow raw embedded JSON → denormalization → served dataset with a content hash in the public manifest (`game-catalog-data` skill). Single-record datasets exist (`lre-common`, until the Stage 3 change removes it).
- `guild-raid-meta` recommendations carry a curated `efficiency` number validated as positive (`GuildRaidMetaValidation`, `InvalidEfficiency`); that is the precedent the Stage 1 follow-up named for curated per-unit tuning.
- The client computes combat power with `@workspace/game-domain` `characterCombatPower` (V1 port) and compares it to `lre-battles` `power`. Battle powers grow roughly geometrically (Uthar Alpha: 1,018; 3,055; 8,199; … 19,994,528 over 18 battles), so a single ratio between team power and battle power is the calibration knob.
- Combat power covers characters only (MoWs are not in Legendary Event teams).
- Legendary Events are horde-style: many waves of weak enemies, where summoners, AoE / multi-hit and sustain outperform their raw power. That strength is a property of the character in horde modes generally, not of one event.

## Goals / Non-Goals

**Goals:**

- One small, curated record the client reads to tune per-character strength without a release.
- Per-character tuning that is easy to curate (a tier) with an escape hatch for fine tuning (a per-character multiplier), reusable by any horde-style mode.
- Safe defaults: untiered characters still produce a labelled estimate.

**Non-Goals:**

- Per-lane or per-event tiers (one tier per character; revisit if feedback shows lane-specific over-performance).
- Serving the power ratio or calibration metadata (one Legendary Event-specific constant, kept in the apps code: design D3).
- Any server computation, aggregation or storage of estimates.
- Renaming the pre-existing `lre-common`, `lres` and `lre-battles` datasets (shipped contracts; a separate change if wanted).

## Decisions

**D1. Horde-mode tier list.** The dataset `horde-mode-tiers` serves exactly one record:

```
{
  id: "horde-mode-tiers",
  tiers: { tier: "S" | "A" | "B" | "C", multiplier: number }[],   // exactly S, A, B, C in that order; multiplier > 0 and <= 5
  characters: { unitId: string, tier: string, multiplier?: number }[]   // ascending by unitId; override > 0 and <= 5
}
```

Denormalization only sorts `characters` by `unitId` (ordinal) so the hash is stable regardless of authoring order, and serves a missing list as empty. A character's effective multiplier is its own `multiplier` when set, else its tier's multiplier; an untiered character has 1.0. The client applies these rules; the server never expands the list. The `multiplier` override is omitted from the served JSON when not set. Rationale: the tier is a property of the character in horde-style modes, so other features can read it without depending on the Legendary Event estimate; a tier is easier to curate and review than a free-form coefficient, and the optional override covers the rare character a tier does not fit, without a fifth tier.

**D2. Defaults.** The file ships the maintainer's horde-mode list: tier multipliers at the midpoint of each tier's range (S 1.75–2.00 → 1.85, A 1.40–1.70 → 1.55, B 1.15–1.35 → 1.25, C 1.00–1.10 → 1.05) and 21 characters, each with its own override (S: Kariyan 2.00, Isabella 1.95, Sho'syl 1.90, Bellator 1.85, Aleph-Null 1.80, Tan Gi'da 1.75; A: Calgar 1.65, Vindicta 1.60, Wrask 1.55, Abraxas 1.50, Nauseous Rotbone 1.45, Re'vas 1.40; B: Eldryon 1.35, Gulgortz 1.30, Archimatos 1.25, Burchard 1.20, Gibbascrapz 1.15; C: Maladus 1.10, Angrax 1.05, Tigurius 1.00, Certus 1.00). The tier value only matters for a character added later without its own override.

**D3. The power ratio is not catalog data.** The Legendary Event estimate also needs a power ratio (team effective power must reach ratio × battle power) and whether it has been calibrated. Review (2026-10-15) dropped the separate `legendary-event-clear-estimate` dataset that served them: it is one Legendary Event-specific number, not a property of characters, and retuning it is a web release, which is cheap. The apps change keeps it as a constant with its calibration flag.

**D4. Validation.** On load: exactly one record with the expected id; `tiers` exactly S, A, B, C in that order; each tier multiplier in (0, 5]; every `unitId` a catalog character id (not a MoW, not an NPC), listed once; every `tier` one of the tier names; a set `multiplier` override in (0, 5] (`InvalidHordeModeTiers`). Failures are catalog load errors naming the offending value, like `GuildRaidMetaValidation`.

**D5. Curation.** The tier list, tier multipliers and overrides are hand-curated by maintainers from estimate-vs-actual evidence (the apps change logs overrides). The `game-catalog-data` skill gets a section: what the numbers mean, the 1.0 baseline, the bounds, when to use an override, and that the evidence goes in the PR. Who owns curation long-term is open (docs feature spec open question).

**D6. Manifest.** The dataset is listed with its hash like every other; the manifest snapshot test changes. No change to `lres` or `lre-battles`.

## Risks / Trade-offs

- [Placeholder multipliers give poor estimates before calibration] → the client shows "uncalibrated" and the user overrides; calibration is part of the apps change.
- [Overrides erode the tier list's simplicity] → documented as fine tuning only, with evidence in the PR.
- [Seed tier list is unverified community opinion] → it is a starting point, curated through catalog PRs like any other value.

## Migration Plan

None (catalog-only). Rollback removes the dataset; the client then treats every character as untiered.

## Open Questions

- Who curates the tier list long-term? Default: maintainers, through catalog PRs.
