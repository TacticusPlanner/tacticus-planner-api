# Design

## Context

See proposal.md. Companion: `tacticus-planner-apps` change `add-goal-season-fit`, whose client resolver projects any `Fixed` definition from `anchorUtc + k * intervalDays` and treats the next cycle start as the season deadline.

## Decisions

1. **Cycle: 14-day interval, 13-day duration, anchor 2026-10-21T05:00:00Z.** Interval and the 05:00 UTC reset come from V1's long-used color coding (`raidSeasonStart = 1_764_738_000_000`, 14 days); the duration matches the existing `config.seasonLengthDays: 13`, which also satisfies the validator's `duration < interval` rule. The anchor is V1's anchor plus 23 intervals, so it names a real recent season rather than a year-old one; the phase is identical.
2. **`endsAt` unchanged.** The projector keeps matching authored occurrences by `season`/`seasonConfigId`. Projected placeholders have no parameters, so they never match; switching `endsAt` to the projection would be a separate behaviour change with its own countdown risk.
3. **No `seasonNumberAtAnchor`.** The upstream season number at the anchor is not known with certainty; the client omits the number when absent.

## Risks / Trade-offs

- [Cadence assumption wrong] -> placeholders and Season fit are off until corrected with one data edit; `endsAt` (authored-only) is unaffected.
- [Manifest snapshot] -> the `event-definitions` hash in the verified snapshot is updated in the same change.
