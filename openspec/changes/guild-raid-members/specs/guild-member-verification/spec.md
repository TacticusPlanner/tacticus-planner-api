## Purpose

Establishes which Planner account really is which guild member, so later features can decide whose data an account may see, and gives guild leaders names for the guild's players.

## ADDED Requirements

### Requirement: A claim is verified only for the account it was verified for

A claim is the link between a guild member and the Planner profile that entered that member's Tacticus user id. A claim SHALL be `pending`, `verified` or `rejected`. A member without a linked profile has no claim.

Verification SHALL apply to one profile on one member. It SHALL end when that profile changes or clears its Tacticus user id, is linked to another member, leaves the guild's roster, or is deleted. A role change or an in-game rename SHALL NOT end it.

An unverified claim SHALL keep the access it has today and SHALL NOT grant anything this capability or a later one ties to verification.

#### Scenario: Id changed and changed back

- **GIVEN** a verified member
- **WHEN** they enter another Tacticus user id, then their own again, and the guild is synchronized
- **THEN** the claim is pending, or verified anew under the rules below

#### Scenario: Promotion keeps verification

- **GIVEN** a verified Officer whom the fresh roster lists as Co-Leader
- **WHEN** the guild is synchronized
- **THEN** the claim stays verified

### Requirement: Matching names verify Officers and Members

A pending claim on an Officer or Member SHALL become verified when the claimant's Tacticus name equals the name assigned to that player. The comparison SHALL ignore case, surrounding spaces and Unicode composition differences.

The Tacticus name SHALL come only from a player sync made with the API key currently saved on the profile. The Planner display name SHALL never count. A claim SHALL NOT be verified this way when:

- no name is assigned;
- two current members of the guild have the same assigned name, or two claimants have the same Tacticus name;
- the member is Leader or Co-Leader;
- the claim is rejected.

Matching SHALL be evaluated after a successful roster save, name change, or the claimant's player sync.

#### Scenario: Assigned name matches

- **GIVEN** a Member assigned the name "Dark" and claimed by a profile whose latest sync says " dark "
- **WHEN** the claimant's next player sync succeeds
- **THEN** the claim is verified

#### Scenario: Claimed id of an unlinked Leader

- **GIVEN** a profile that entered the id of a Leader, and whose synced name equals the Leader's assigned name
- **WHEN** the guild is synchronized
- **THEN** the claim stays pending

#### Scenario: Key replaced after the sync

- **GIVEN** a claimant synced as "Dark" and then saved another API key
- **WHEN** a leader assigns "Dark" to the claimed member
- **THEN** the claim stays pending until a sync with the new key

#### Scenario: Display name is not proof

- **GIVEN** a claimant with Planner display name "Dark" and no player sync
- **WHEN** the claimed member is assigned "Dark"
- **THEN** the claim stays pending

### Requirement: The stored key establishes the first verified leader

After a successful roster save, when the guild has no verified Leader or Co-Leader, the claim of the profile that stored the guild's current key SHALL become verified if its member is currently Leader or Co-Leader and the claim is not rejected.

#### Scenario: New registration

- **WHEN** a Co-Leader registers a guild
- **THEN** their claim is verified

#### Scenario: Existing guild

- **GIVEN** a guild registered before this change by a profile that is still Leader
- **WHEN** its roster is next saved
- **THEN** that profile's claim is verified

#### Scenario: The only verified leader left

- **GIVEN** the only verified leader left the guild
- **WHEN** another Co-Leader stores a working key for it
- **THEN** that Co-Leader's claim is verified

### Requirement: Verified leaders manage claims

Only a caller whose own claim is verified and whose persisted role is Leader or Co-Leader SHALL read or change claims and names. Every other linked caller SHALL receive 403 with `issueCode` `guildVerificationRequired`; a caller without a linked registered guild SHALL receive 409 `guildNotLinked`; an unprovisioned profile SHALL receive 404. These endpoints SHALL make no upstream call and SHALL only reach members and players of the caller's guild; any other id is 404.

- `POST /api/v1/guild/members/{guildMemberId}/confirm` SHALL verify the claim, whatever its state. It SHALL assign the claimant's current Tacticus name when the player has no assigned name and that name is known.
- `POST /api/v1/guild/members/confirm` SHALL take `claims`, 1 to 100 pairs of `guildMemberId` and `profileClaimId`, and confirm each pending claim on a player without an assigned name. It SHALL return `confirmed` and `skipped` arrays of guild member ids and change nothing about the skipped ones, which include changed claims and the caller's own.
- `POST /api/v1/guild/members/{guildMemberId}/reject` SHALL reject a pending or verified claim.

The single confirm and reject SHALL carry the `profileClaimId` the caller was shown, and SHALL answer 409 `guildClaimChanged` and change nothing when another account holds the claim now or there is none. A `profileClaimId` SHALL NOT be a profile id or hash and SHALL be meaningless outside its guild.

Nobody SHALL confirm or reject their own claim (403 `forbidden`). Only a verified Leader SHALL reject the verified claim of a Leader or Co-Leader (403 `forbidden`).

#### Scenario: Pending leader

- **GIVEN** a caller with a pending claim on the Leader
- **WHEN** they read the players
- **THEN** the response is 403 `guildVerificationRequired`

#### Scenario: Claimant changed while the leader decided

- **GIVEN** a leader is shown a claim by account A
- **WHEN** account B holds the claim by the time the leader confirms it
- **THEN** the response is 409 `guildClaimChanged` and B is not verified

#### Scenario: Confirm all skips mismatches

- **GIVEN** one pending claim without an assigned name and one whose assigned name differs from the synced name
- **WHEN** both are sent to the bulk confirm
- **THEN** the first is confirmed and the second is skipped

#### Scenario: Co-Leader against the Leader

- **WHEN** a verified Co-Leader rejects the verified Leader's claim
- **THEN** the response is 403 and the claim stays verified

### Requirement: A rejected claim frees the id

Rejecting SHALL end verification, remove the link and the copies derived from it, and clear the claimant's Tacticus user id if it is still the member's id, so that another account can enter it. The claimant's other data SHALL NOT change.

If the same profile claims the same member again, the claim SHALL be `rejected`: never verified automatically, and verified only when a verified leader confirms it.

#### Scenario: The real owner claims after an impostor

- **GIVEN** an impostor's claim was rejected
- **WHEN** the real owner enters the id and the guild is synchronized
- **THEN** the save succeeds and the owner's claim is pending

#### Scenario: Rejected by mistake

- **GIVEN** a rejected claimant who entered the id again
- **WHEN** the guild is synchronized and a verified leader confirms the claim
- **THEN** the claim is verified

### Requirement: Guild players keep an identity and a name

Every Tacticus player seen on a guild's roster or in its stored raid hits SHALL have one player record in that guild, with a stable random id that outlasts roster membership. It SHALL hold the keyed hash, the last four characters of the Tacticus user id when known, and an optional assigned name. It SHALL be deleted with the guild's registration.

`PUT /api/v1/guild/players/{playerId}/name` with `name` (string or null) SHALL set or clear the assigned name. A name SHALL be trimmed and be 1 to 50 characters without control characters, else 400. Duplicates SHALL be allowed. Changing a name SHALL NOT end a verification.

#### Scenario: Former member

- **GIVEN** a player with hits in season 90 who is no longer on the roster
- **WHEN** a verified leader assigns them a name
- **THEN** the name is kept and they remain listed without a membership

### Requirement: Verified leaders can read the guild's players

`GET /api/v1/guild/players` SHALL return `players`, current members first in roster order and then former ones by last hit, newest first, each with:

- `playerId`: GUID;
- `maskedTacticusUserId`: string, or null when the last four characters are unknown;
- `assignedName`: string or null;
- `lastHitAt`: UTC timestamp or null;
- `member`: null for a former member, else `guildMemberId` (GUID), `role`, `level` (integer) and `claim`.

`claim` SHALL be null without a linked profile, else:

- `profileClaimId`: string;
- `state`: `pending`, `verified` or `rejected`;
- `method`: `key`, `nameMatch` or `leader` when verified, else null;
- `tacticusName`: the name from a sync with the current key, or null;
- `nameMatch`: `noAssignedName`, `noTacticusName`, `matches`, `differs` or `duplicate`;
- `isCaller`: boolean;
- `canReject`: boolean.

No profile id, display name, hash, full Tacticus user id or key SHALL be returned.

#### Scenario: Member without an account

- **WHEN** a verified leader reads a member nobody claims
- **THEN** it has a masked id, its role and level, and a null claim

### Requirement: Callers can see where they stand

`GET guild` SHALL add to a registered guild: `callerClaimState` (`pending`, `verified` or `rejected`), `canManageMembers` (boolean) and `pendingClaimCount` (integer, 0 unless the caller can manage members). Register and sync responses SHALL carry the same fields. Assigned names SHALL NOT appear in these responses.

#### Scenario: Waiting member

- **GIVEN** a Member with a pending claim
- **WHEN** they read `GET guild`
- **THEN** the guild is returned as before with `callerClaimState` `pending`

### Requirement: The roster refreshes itself daily

After `POST guild/raid-status/refresh` persists an observation, with or without an active season, the same operation SHALL refresh the roster with the stored key when its last successful sync is at least 24 hours old. Attempts that this starts SHALL be at least one hour apart per guild, across all API instances, and SHALL count as sync attempts for the guild sync cooldown. The refresh response SHALL NOT change because of their outcome. Nothing SHALL run in the background.

The roster refresh SHALL save members, roles and links as a sync does, without a caller. It SHALL save nothing when the key answers for another guild or the response is incomplete, rejected or unavailable. It SHALL NOT replace the key.

#### Scenario: Kicked member

- **GIVEN** a verified member kicked in game, and a roster synced 25 hours ago
- **WHEN** another member's raid refresh succeeds
- **THEN** the kicked member is no longer linked when the refresh responds

#### Scenario: Roster refresh fails

- **GIVEN** a roster synced 25 hours ago
- **WHEN** a raid refresh succeeds and the roster request times out
- **THEN** the refresh returns its successful result and the roster is not requested again for an hour

#### Scenario: Recent roster

- **GIVEN** a roster synced 3 hours ago
- **WHEN** a raid refresh succeeds
- **THEN** no roster request is made

#### Scenario: Key of another guild

- **WHEN** the stored key answers for another guild
- **THEN** no member, role or name of either guild changes
