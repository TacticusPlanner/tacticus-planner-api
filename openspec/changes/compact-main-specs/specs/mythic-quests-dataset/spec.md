## MODIFIED Requirements

### Requirement: The catalog serves a `mythic-quests` dataset

The game catalog SHALL serve a dataset keyed `mythic-quests` as a plain array. It SHALL be built from the quests named `mythic_{characterId}_{number}` in the `hero` group
of the pinned datamine copy `gameconfig/quests/groups.json`, keeping each quest's `rewards` and each task's
`locaKey`, `target` and `taskParameters`; a `mythic_` quest whose name does not fit that pattern SHALL fail
startup naming `mythic-quests`. The catalog SHALL keep no
hand-kept mythic-quest file. Task text templates SHALL NOT be part of the catalog: task text is display text
and stays with the client.

#### Scenario: Dataset is served without authentication

- **WHEN** a client requests the `mythic-quests` endpoint
- **THEN** the array is returned without requiring authentication, wrapped in the same envelope as the other
  served datasets

#### Scenario: Dataset appears in the manifest

- **WHEN** the game catalog manifest is requested
- **THEN** it includes a hash entry for `mythic-quests`, and the manifest snapshot test covers it

#### Scenario: The raw source has no direct endpoint

- **WHEN** the set of game catalog endpoints is enumerated
- **THEN** there is no endpoint serving the raw `groups.json` directly

#### Scenario: A misnamed mythic quest fails startup

- **GIVEN** a `hero` quest named `mythic_blackAbaddon`
- **WHEN** the catalog loads
- **THEN** loading fails naming `mythic-quests`
