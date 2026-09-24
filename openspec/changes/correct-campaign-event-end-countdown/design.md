## Context

See `proposal.md` and `specs/game-events-calendar-dataset/spec.md`. Raw `Data/events/event-definitions.json` defines `campaign-event` as a fixed 35-day interval with nominal 14-day duration from a UTC anchor; `Data/events/event-occurrences.json` has only an earlier authored campaign-event occurrence. `EventsDenormalizer.BuildEventsCalendar` projects a rolling 15-week window and lets an overlapping authored occurrence supersede a placeholder. The served entry already carries `confirmed` and `occurrenceId`. The screenshot reports two relative times but no reliable absolute timestamp or timezone.

## Goals / Non-Goals

**Goals:** Record a verified actual window as one confirmed catalog occurrence, keep unverified windows projected and marked unconfirmed, and make the corrected data available through the existing manifest/cache path.

**Non-Goals:** Do not invent an eight-hour correction, change the global recurrence based on one report, add display text to catalog records, or create a second calendar projection algorithm.

## Decisions

1. First obtain dated in-game evidence identifying the event and its end instant, including timezone or enough information to convert to UTC; compare it at the same instant with the served `events-calendar` and the app's displayed phrase. If the source does not establish a boundary, do not author an occurrence claiming confirmation. Record the evidence in the change's verification record during apply. A broader cadence discrepancy would require revising the paired specs/design before code changes.
2. Once verified, add one raw `event-occurrences.json` record for the affected slot using the observed UTC boundaries. Keep the definition's 35-day cadence/14-day nominal duration and 15-week horizon unless independently disproved. The existing overlap-supersession path is the canonical served result; do not special-case Tyranid, change every future slot, or perform client-side timestamp offsets.
3. Preserve the served `events-calendar` shape (`occurrenceId`, `definitionId`, `confirmed`, `startUtc`, `endUtc`, `parameters`) and raw/served split. `event-definitions.json` supplies the recurrence, raw `event-occurrences.json` supplies confirmed boundaries, and `EventsDenormalizer` indexes the resulting entry by date. The companion apps change consumes the already-present `confirmed` flag from the same dataset. There is no EF Core schema change, migration, or backfill; no endpoint/OpenAPI shape change is expected.
4. A content-only correction changes the `events-calendar` payload hash and manifest source hash; keep `SchemaVersion` unchanged, update `Version` as a catalog release tag, and change `GameVersion` only if the source reflects a distinct in-game version. Review the API manifest snapshot (time-dependent event hash may be scrubbed) and add deterministic denormalization tests proving overlap replacement and later slots' projection.

## Risks / Trade-offs

- [Evidence may not contain a reliable absolute end] → Leave the slot unconfirmed and block the API data task until a verifiable source exists; the paired app can still stop showing a false exact countdown for projections.
- [An authored window may overlap the wrong slot or a neighboring event] → Test the affected and adjacent slots at fixed injected times and validate uniqueness/UTC ordering before release.
- [Catalog content update can remain cached] → Check the served manifest/content hash and a client refresh against the paired app change.

## Open Questions

- What exact UTC start/end and event identity does dated in-game evidence establish for the reported Tyranid window? The answer fills the authored data value without changing the contract or task sequence. If investigation instead disproves the fixed recurrence model, stop and revise both proposals before implementing a different correction.

## Apply investigation — 2026-09-24

The supplied feedback screenshot establishes a mismatch between two relative countdowns (3 hours in Dailies versus 11 hours in-game), but it does not show the capture date, an absolute clock, or a timezone. The public September 2026 Tacticus release notes reviewed during apply list other event dates but do not establish the Tyranid campaign-event end instant: https://apps.apple.com/tt/app/warhammer-40-000-tacticus/id1599937506?platform=ipad. Community discussion indicates a September campaign-event window, but is not a dated in-game or official source for its exact UTC boundaries. Accordingly, API task 1.1 remains open; no `startUtc`/`endUtc` value or eight-hour offset has been inferred. Obtain a dated in-game capture showing the Tyranid event identity and its remaining time alongside an absolute device time and timezone, or an official announcement stating the exact end time, before changing catalog data.
