## ADDED Requirements

### Requirement: V1 Survival teams are imported

The teams part SHALL also read V1 `survivalTeams` (event name to unit ids). Each entry with at least one unit SHALL become a Horde team after the `teams2` teams, named `Survival · <Event>` (`Survival · December 2026`), with `survivalEventKey` the event's served id, characters and MoWs split by what each unit resolves to, the featured hero dropped, and import key `survival:<event name>`. An entry for an event the catalog does not serve SHALL be skipped with `survival_event_not_recognised`.

#### Scenario: Crescendo 01

- **WHEN** V1 holds a team for `season_crescendo_01_event`
- **THEN** its outcome is Skipped with `survival_event_not_recognised` and no team is created
