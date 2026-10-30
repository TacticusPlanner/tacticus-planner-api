# tacticus-user-id Specification

## Purpose
How a profile's Tacticus user id is validated, normalized and kept unique, and how changing it affects guild membership.

## Requirements

### Requirement: A Tacticus user id is a GUID stored in canonical form

`PUT account/tacticus-integration` and the V1 import SHALL accept a Tacticus user id only when its trimmed value parses as a GUID. They SHALL store it in canonical lower-case hyphenated form, and SHALL hash that form. A non-GUID SHALL be a 400 naming `tacticusUserId`; in the import, the part fails with `invalid_tacticus_user_id`. The id is self-asserted: its ownership is not proven.

#### Scenario: Upper-case GUID is canonicalized

- **WHEN** the caller saves `tacticusUserId` `"  3F2504E0-4F89-11D3-9A0C-0305E82C3301 "`
- **THEN** the stored id is `3f2504e0-4f89-11d3-9a0c-0305e82c3301`

#### Scenario: Not a GUID

- **WHEN** the caller saves `tacticusUserId` `"player-42"`
- **THEN** the response is 400 `validation` naming `tacticusUserId`, and the stored id is unchanged

### Requirement: One Tacticus user id per profile

A Tacticus user id SHALL be linked to at most one profile:

- When another profile already holds the id, `PUT account/tacticus-integration` SHALL respond 409 with `issueCode` `tacticusUserIdInUse` and change nothing.
- In the V1 import, the Tacticus user id part SHALL fail with code `tacticus_user_id_conflict`.

#### Scenario: Id already claimed

- **GIVEN** profile A holds Tacticus user id X
- **WHEN** profile B saves X
- **THEN** the response is 409 `tacticusUserIdInUse` and profile B's id is unchanged

### Requirement: Changing or clearing the id unlinks the guild member

When a save changes or clears the profile's Tacticus user id, the profile's guild-member link SHALL be severed in the same save. Severing clears its profile link, linked player name and last-active time. This applies to `PUT account/tacticus-integration` and to the V1 import. The next guild sync SHALL relink a member only from the new id. Saving the same id again SHALL NOT unlink.

#### Scenario: Clearing drops guild authority

- **GIVEN** a profile linked to a guild as Co-Leader
- **WHEN** it clears its Tacticus user id
- **THEN** the profile is no longer linked to any guild member, and guild sync and purge return 403
