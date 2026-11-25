## MODIFIED Requirements

### Requirement: Guild reads may use the persisted role until the next sync

Guild reads SHALL be authorized from the persisted link and role without an upstream call. These reads are `GET guild`, `GET guild/raid-status` and the raid refresh. A member who has departed or been demoted upstream MAY keep this access until the next guild sync revokes it. A profile SHALL be linked to at most one guild. A guild sync whose fresh roster lists a profile's Tacticus user SHALL move that profile's link to the synchronized guild, severing a link left on another guild's roster.

#### Scenario: Departed member before the next sync

- **GIVEN** a member removed from the guild upstream, before any guild sync has run
- **WHEN** they send `GET guild`
- **THEN** the response is 200 with the guild as last synchronized

#### Scenario: Member who moved guilds

- **GIVEN** a profile linked on guild A's roster, and guild A has not synced since the player moved to guild B
- **WHEN** guild B is registered or synchronized with a fresh roster that lists the player
- **THEN** the request succeeds, the profile is linked to guild B, and guild A's member row is no longer linked
