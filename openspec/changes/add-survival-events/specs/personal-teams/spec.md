## ADDED Requirements

### Requirement: A team can be tied to a Survival event

A team SHALL carry a nullable `survivalEventKey` on create, update and every read. When it is set, these rules SHALL apply and be reported like the other team rules, in this order after the existing codes:

| Code | Broken when |
| --- | --- |
| `survivalModeRequired` | the modes are not exactly Horde |
| `unknownSurvivalEvent` | the key is not a served `survival-events` id |
| `survivalFeaturedCharacter` | the characters include the event's featured hero |
| `survivalTooManyCharacters` | there are more characters than the event's `maxCharacters` |
| `survivalTooManyMows` | there are more MoWs than the event's `maxMows` (when it allows any) |

The existing `mowsNotAllowed` SHALL be reported when the event allows no MoWs and the team has some, and `factionNotAllowed` when a character's faction is not in the event's `allowedFactionIds`. The key SHALL stay after the event ends; the team is labelled by it, never hidden.

#### Scenario: The featured hero is implied

- **WHEN** a December 2026 Survival team stores `admecManipulus`
- **THEN** the save is refused with `survivalFeaturedCharacter`
