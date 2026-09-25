## Why

A player reports that Dailies says a Tyranid campaign event ends in about three hours while the game says about eleven. The current calendar window is a recurrence projection, not a verified end time, so the catalog must distinguish a confirmed occurrence from a placeholder before the client presents an exact countdown.

## What Changes

- Verify the reported event's identity and UTC boundaries against dated in-game evidence; do not assume an eight-hour offset from the screenshot alone.
- Author the confirmed campaign-event occurrence for the affected slot once its boundaries are verified, superseding the overlapping projected placeholder. Keep the 35-day cadence, 14-day nominal duration, and rolling 15-week projection unless the evidence establishes a broader schedule error.
- Preserve the existing `confirmed`/`occurrenceId` distinction in the served `events-calendar`; no new endpoint or payload field is planned. The companion apps change uses that distinction to avoid treating an unconfirmed projection as an exact end time.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `game-events-calendar-dataset`: A confirmed campaign-event end time is sourced from a verified authored occurrence, with a projected window remaining explicitly unconfirmed until then.

## Impact

- Raw `event-occurrences.json`, event denormalization/validation tests, and the `events-calendar` catalog hash; no EF migration or OpenAPI shape change anticipated.
- Companion `tacticus-planner-apps/openspec/changes/correct-campaign-event-end-countdown`; API applies first.
