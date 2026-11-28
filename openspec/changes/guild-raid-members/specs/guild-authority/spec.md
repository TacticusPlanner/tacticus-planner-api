## MODIFIED Requirements

### Requirement: Guild purge is pre-gated without an upstream call

`DELETE guild` SHALL respond 403, with no upstream call and nothing deleted, when any of these is missing:

- a configured Tacticus user id;
- a link to a registered guild as a persisted Leader or Co-Leader;
- a stored Guild API token for that guild;
- a verified claim of the caller (see `guild-member-verification`), in which case `issueCode` SHALL be `guildVerificationRequired`.

#### Scenario: Cleared Tacticus user id

- **GIVEN** a former Co-Leader who has cleared their Tacticus user id
- **WHEN** they send `DELETE guild`
- **THEN** the response is 403 and the guild still exists

#### Scenario: Plain member does not spend the token

- **GIVEN** a caller persisted as a plain member
- **WHEN** they send `DELETE guild`
- **THEN** the response is 403 and no upstream request is made

#### Scenario: Someone else's Leader id

- **GIVEN** a caller who entered an unclaimed Leader's id and is persisted as Leader with a pending claim
- **WHEN** they send `DELETE guild`
- **THEN** the response is 403 `guildVerificationRequired`, no upstream request is made and the guild still exists

### Requirement: Guild purge re-verifies the caller against a fresh upstream roster

After the pre-gate, `DELETE guild` SHALL synchronize the guild with its stored token. It SHALL delete the registration, roster and raid history only when that sync succeeds, lists the caller as Leader or Co-Leader of the same guild, and leaves the caller's claim verified. A persisted role SHALL never authorize the purge on its own.

#### Scenario: Current leader purges

- **GIVEN** a verified caller linked as Co-Leader whom the fresh upstream roster still lists as Co-Leader
- **WHEN** the caller sends `DELETE guild`
- **THEN** the response is 204 and the guild and its member rows are deleted

#### Scenario: Demoted upstream, not yet re-synced

- **GIVEN** a verified caller persisted as Co-Leader whom the upstream roster now lists as a plain member
- **WHEN** the caller sends `DELETE guild`
- **THEN** the response is 403 and the guild still exists

### Requirement: Guild reads may use the persisted role until the next sync

Guild reads SHALL be authorized from the persisted link and role without an upstream call. These reads are `GET guild`, `GET guild/raid-status`, the raid refresh, and the claim and player endpoints of `guild-member-verification`, which also require a verified claim. A member who has departed or been demoted upstream MAY keep this access until the next guild sync, including the automatic roster refresh, revokes it. A profile SHALL be linked to at most one guild.

#### Scenario: Departed member before the next sync

- **GIVEN** a member removed from the guild upstream, before any guild sync has run
- **WHEN** they send `GET guild`
- **THEN** the response is 200 with the guild as last synchronized

## ADDED Requirements

### Requirement: Replacing the key needs a verified leader

`POST guild/register` for a guild that has a verified Leader or Co-Leader in the fresh roster SHALL succeed only when the caller's own claim is verified. Otherwise it SHALL respond 403 `guildVerificationRequired` and persist nothing, including the roster. Without a verified leader, any current Leader or Co-Leader MAY register, as before.

A registration that keeps the stored key, as the V1 import does for a guild that already exists, SHALL NOT verify the caller or change who stored the key.

#### Scenario: Pending Co-Leader replaces the key

- **GIVEN** a guild with a verified Leader and a caller with a pending claim on a Co-Leader
- **WHEN** the caller registers the guild with a working key
- **THEN** the response is 403 `guildVerificationRequired` and the stored key is unchanged

#### Scenario: Verified Co-Leader replaces the key

- **WHEN** a verified Co-Leader registers the guild with a new working key
- **THEN** the key is replaced

#### Scenario: No verified leader left

- **GIVEN** no verified Leader or Co-Leader is on the fresh roster
- **WHEN** a Leader registers the guild with a working key
- **THEN** the key is replaced and their claim is verified
