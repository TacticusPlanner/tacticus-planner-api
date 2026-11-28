## MODIFIED Requirements

### Requirement: Account purge removes guild-visible copies

`DELETE account` SHALL clear the guild-visible copies derived from the profile, in the same transaction that deletes the profile and every row it owns. These copies are the linked player name and the last-active-in-Planner time on the profile's guild-member row, the member's profile link and claim state, and, when the claim was verified, the name assigned to that player. Guildmates SHALL stop seeing them at once, without waiting for a guild sync. The guild's stored token, player records and raid hits are not affected.

#### Scenario: Guildmate no longer sees the purged name

- **GIVEN** profile A is linked to a guild member that a guildmate sees as "A's name"
- **WHEN** A sends `DELETE account`
- **THEN** the guildmate's next `GET guild` shows that member with no linked player name and no last-active time

#### Scenario: Assigned name of a verified account

- **GIVEN** A's claim is verified and a leader assigned the player a name
- **WHEN** A sends `DELETE account`
- **THEN** the player has no assigned name and no claim, and their hits remain

#### Scenario: Assigned name under a false claim

- **GIVEN** A's claim on a named player is pending
- **WHEN** A sends `DELETE account`
- **THEN** the player keeps the assigned name
