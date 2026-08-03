## Purpose

Ensures authenticated Tacticus synchronization detects and publishes player-content changes independently from changes to the game's configuration version.

## ADDED Requirements

### Requirement: Player content is evaluated on every successful upstream sync
The system SHALL evaluate the player content returned by every successful upstream synchronization even when the returned game-configuration hash matches the previously stored value.

#### Scenario: Shards change under the same game configuration
- **WHEN** the upstream response contains changed shard amounts and the same game-configuration hash as the stored snapshot
- **THEN** the system persists the changed shard data and publishes a new hash for the affected player-data chunk

#### Scenario: Roster progression changes under the same game configuration
- **WHEN** the upstream response contains changed unit progression and the same game-configuration hash as the stored snapshot
- **THEN** the system persists the changed roster data and publishes a new hash for the affected player-data chunk

### Requirement: Chunk persistence is driven by transformed content
The system SHALL derive each player-data chunk hash from the transformed chunk content and SHALL replace a stored chunk only when that content hash changes.

#### Scenario: Only one chunk changes
- **WHEN** a successful synchronization changes the content of exactly one player-data chunk
- **THEN** the manifest advertises a new hash for that chunk and retains the existing hashes for every unchanged chunk

#### Scenario: Player content is unchanged
- **WHEN** a successful synchronization returns player content identical to the stored snapshot
- **THEN** the system retains all existing chunk hashes and payloads while recording the successful synchronization

### Requirement: Configuration and player freshness metadata remain distinct
The system SHALL treat the upstream game-configuration hash as configuration metadata rather than as player-content identity, and SHALL record the upstream player-data freshness timestamp supplied by the successful response.

#### Scenario: Configuration remains unchanged while player data advances
- **WHEN** the upstream player freshness timestamp and player content advance without a game-configuration change
- **THEN** the stored freshness timestamp and affected player-data chunks advance without requiring a new game-configuration hash

### Requirement: Existing stale snapshots recover through normal synchronization
The system SHALL repair a previously stale stored snapshot during the next successful synchronization without requiring a database migration or client cache reset.

#### Scenario: Corrected server synchronizes a stale shard snapshot
- **WHEN** a stored snapshot contains old shard counts and a normal synchronization receives newer counts under the same game-configuration hash
- **THEN** the server updates the snapshot and returns a manifest whose changed chunk hash causes existing delta-sync clients to download the corrected shard data

### Requirement: Simultaneous farm rewards have one combined expected yield
The game catalog SHALL represent rewards for the same resource and battle as one farm location. When a battle awards a guaranteed copy and one or more probabilistic copies of that resource simultaneously, the location's effective rate SHALL equal the guaranteed yield plus every probabilistic expected rate.

#### Scenario: Elite character shard has a guaranteed shard and bonus chance
- **GIVEN** an elite battle awards one guaranteed character shard and a `shard_elite` bonus with effective rate `0.079`
- **WHEN** character shard farm locations are denormalized
- **THEN** the catalog contains one location for that character and battle with `guaranteed` set and an effective rate of `1.079`

#### Scenario: A reward occurs only once in a battle
- **GIVEN** a battle has only one guaranteed or probabilistic occurrence of a resource
- **WHEN** farm locations are denormalized
- **THEN** its existing guaranteed and drop-chance semantics remain unchanged
