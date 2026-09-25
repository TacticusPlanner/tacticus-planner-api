## 1. Establish authoritative event time

- [ ] 1.1 Capture dated in-game or official event evidence for the reported campaign-event identity and end time, convert it to UTC, and compare the same instant with the served `events-calendar` entry and Dailies phrase; record the source, date, timezone, and comparison in the change verification record. Do not mark this task complete from the relative-time screenshot alone.
- [ ] 1.2 Check whether the evidence indicates a one-slot drift or contradicts the 35-day/14-day recurrence; verify the comparison against adjacent calendar slots. If the recurrence itself is wrong, revise the paired proposal/specs/design before continuing rather than applying a one-off offset.

## 2. Catalog correction

- [ ] 2.1 Author the affected slot's verified `startUtc`/`endUtc` in `Data/events/event-occurrences.json` with the existing `campaign-event` definition ID and a stable occurrence ID; verify raw-data validation and that no unsupported display text or icon fields are added.
- [ ] 2.2 Add deterministic `EventsDenormalizerTests` for the corrected slot, its overlapping placeholder's absence, the authored `confirmed: true` entry, and adjacent unverified slots remaining projected `confirmed: false`; verify the focused game-catalog test project passes.
- [ ] 2.3 Update the catalog release tag for the content correction, leaving `SchemaVersion` unchanged and only changing `GameVersion` if evidence identifies a distinct game version. Verify the served `events-calendar` hash changes, review the API manifest snapshot diff (including its time-dependent-hash scrub), and confirm the existing endpoint shape remains unchanged; coordinate a refreshed catalog with the apps companion.

## 3. Integration and gates

- [ ] 3.1 With the Aspire stack, compare the served confirmed occurrence and refreshed apps status at a fixed testable instant, and verify a later unverified slot remains visibly unconfirmed; record the exact evidence and result in verification notes.
- [ ] 3.2 Run `dotnet format TacticusPlanner.slnx --verify-no-changes --no-restore`, `dotnet build TacticusPlanner.slnx -c Release --no-restore`, `dotnet test TacticusPlanner.slnx -c Release --no-build`, and `git diff --check`; verify all gates pass before applying the apps companion.
