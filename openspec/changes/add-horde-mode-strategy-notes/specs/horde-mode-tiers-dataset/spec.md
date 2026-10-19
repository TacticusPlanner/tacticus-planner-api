## ADDED Requirements

### Requirement: Horde-mode characters may list synergies

Each `horde-mode-tiers` character entry MAY carry `synergies?: string[]`: recommended teammates as catalog character ids, served in authoring order and omitted when missing or empty. No strategy text is served; the client resolves names, portraits and all strategy text.

#### Scenario: Seed synergies

- **WHEN** the client reads `horde-mode-tiers`
- **THEN** `blackTerminator` carries `synergies` `["deathRotbone", "worldTerminator", "blackAbaddon", "deathBlightlord"]` in that order and `custoBladeChampion` has no `synergies` field

### Requirement: Synergies are validated on load

The catalog loader SHALL reject the raw `horde-mode-tiers` file, with an `InvalidHordeModeTiers` error naming the character and the offending value, when a synergy is not a catalog character id, is the character itself, appears twice in one list, or a list has more than 6 entries.

#### Scenario: Unknown synergy

- **GIVEN** `blackTerminator` lists synergy `notAUnit`
- **WHEN** the catalog loads
- **THEN** loading fails naming `blackTerminator` and `notAUnit`

#### Scenario: Self synergy

- **GIVEN** `blackTerminator` lists synergy `blackTerminator`
- **WHEN** the catalog loads
- **THEN** loading fails naming `blackTerminator`
