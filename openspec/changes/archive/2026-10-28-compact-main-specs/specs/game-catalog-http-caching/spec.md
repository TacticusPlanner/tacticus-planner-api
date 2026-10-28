## ADDED Requirements

### Requirement: Every served dataset is public and listed in the manifest

Every served game-catalog dataset SHALL be served at its own `GET /api/v1/game-catalog/{dataset}` endpoint without authentication, in the shared dataset envelope, and SHALL have a content-hash entry in the public manifest. Raw source files SHALL NOT be served directly.

#### Scenario: A dataset is public and hashed

- **WHEN** a client reads the manifest and then any dataset it lists, without credentials
- **THEN** the dataset is returned in the shared envelope and its hash equals the manifest entry, and no endpoint serves a raw source file

### Requirement: Served records carry ids, not presentation

Served records SHALL carry ids and structured values only, with no display name, icon, icon id or wiki link; the client resolves presentation from ids. The only exception is a field whose own dataset spec requires the game's untranslated text (e.g. `upgrades` `label` and `material`).

#### Scenario: A record carries no presentation

- **WHEN** a client reads the `raid-bosses` or `shops` dataset
- **THEN** no record carries a display name, icon, icon id or wiki link

#### Scenario: Untranslated game names are the stated exception

- **WHEN** a client reads the `upgrades` dataset
- **THEN** `label` and `material` carry the game's untranslated names, as `upgrades-dataset` requires
