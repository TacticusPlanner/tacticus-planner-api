## MODIFIED Requirements

### Requirement: Guild reads may use the persisted role until the next sync

Guild reads SHALL be authorized from the persisted link and role without an upstream call. These reads are `GET guild`, `GET guild/raid-status`, `GET guild/raid/seasons` and the raid refresh. A member who has departed or been demoted upstream MAY keep this access until the next guild sync revokes it. A profile SHALL be linked to at most one guild.

#### Scenario: Departed member before the next sync

- **GIVEN** a member removed from the guild upstream, before any guild sync has run
- **WHEN** they send `GET guild`
- **THEN** the response is 200 with the guild as last synchronized
