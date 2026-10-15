## 1. Data

- [x] 1.1 Re-time every HSE occurrence in `event-occurrences.json` to 01:00 Europe/Prague on its listed start and end dates (23:00Z the day before under CEST, 00:00Z under CET).
- [x] 1.2 Shorten Against the Tide (October) to end 2026-10-15T23:00Z and Psyker Boost to end 2026-10-21T23:00Z.

## 2. Tests and spec

- [x] 2.1 Update the expected instants in `HomeScreenEventCalendarTests.HseDefinitionsAndUpdate142And143OccurrencesExist`.
- [x] 2.2 Update the `game-events-calendar-dataset` convention and scenarios.

## 3. Verification

- [ ] 3.1 `dotnet test` for `TacticusPlanner.GameCatalog.Tests` passes in CI (the .NET SDK is not available in the authoring session).
- [ ] 3.2 Check the next HSE switch in game against the authored instant.
