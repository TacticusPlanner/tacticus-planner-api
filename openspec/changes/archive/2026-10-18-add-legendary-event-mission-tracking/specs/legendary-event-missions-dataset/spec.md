## ADDED Requirements

### Requirement: Served missions are typed

Every served `lres` record SHALL carry `regularMissions` and `premiumMissions` as arrays of `{ index, kind, count, faction, alliance, damageType, trait, text }` in raw order, where `index` is the 0-based position, `text` the raw line, `kind` one of `anyBattle`, `defeatFaction`, `useAbilitiesWithAlliance`, `damageType`, `defeatWithAlliance`, `winWithoutTrait`, `winWithoutSummon`, `defeatWithDamageType`, and the parameters the kind does not use are null.

#### Scenario: Uthar missions

- **WHEN** the `lres` dataset is served
- **THEN** `votanUthar` regular mission 1 is `{ index 1, kind "defeatFaction", count 30, faction "Necrons", text "Defeat 30 Necrons" }`, regular mission 9 is `{ kind "damageType", count 15, damageType "Bolter" }`, and premium mission 4 is `{ kind "winWithoutTrait", count 5, trait "BigTarget" }`

#### Scenario: Waves with plain damage

- **WHEN** the dataset is served
- **THEN** every event's regular mission 0 ("Defeat 5 waves of enemies in any wave-based game mode, Deal 1000 damage") has kind `anyBattle` and count 5

### Requirement: The loader rejects unparsed missions

The catalog loader SHALL reject a raw `lres-*.json` whose mission line matches no template, or names a faction, alliance, trait or damage type with no catalog mapping, naming the event, the source file, the list and the line.

#### Scenario: New template

- **GIVEN** a raw file with the premium mission "Win 3 battles with only Tyranids"
- **WHEN** the catalog loads
- **THEN** loading fails naming that file, `premiumMissions` and the line
