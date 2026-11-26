## ADDED Requirements

### Requirement: A team can be tied to a Survival event

A team SHALL carry a nullable `survivalEventKey` on create, update and every read. When it is set, these rules SHALL apply and be reported like the other team rules, in this order after the existing codes:

| Code | Broken when |
| --- | --- |
| `survivalModeRequired` | the modes are not exactly Horde |
| `unknownSurvivalEvent` | the key is not a served `survival-events` id |
| `survivalFeaturedCharacter` | the characters include the event's featured hero |
| `survivalTooManyCharacters` | there are more characters than the event's `maxCharacters` |

The existing `mowsNotAllowed` SHALL be reported when the event's `mowsAllowed` is false and the team has some MoWs (an event that allows them adds no MoW cap of its own), and `factionNotAllowed` when a character's faction is not in the event's `allowedFactionIds`. The key SHALL stay after the event ends; the team is labelled by it, never hidden. An update whose body omits `survivalEventKey` SHALL keep the stored key; an explicit `null` SHALL clear it. When an update keeps a stored key the catalog no longer serves, the key SHALL be only a label: the Survival rules SHALL be skipped and no code reported, on the update and on every read. A create, or an update that changes the key, SHALL still report `unknownSurvivalEvent` for an unserved key.

#### Scenario: The featured hero is implied

- **WHEN** a December 2026 Survival team stores `admecManipulus`
- **THEN** the save is refused with `survivalFeaturedCharacter`

#### Scenario: An older client keeps the key

- **WHEN** an update body has no `survivalEventKey` property
- **THEN** the team keeps its stored key

#### Scenario: The event left the catalog

- **WHEN** a team's stored key is no longer a served `survival-events` id and an update renames it, keeping the key
- **THEN** the update succeeds with no Survival code and the key is unchanged
