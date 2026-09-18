# player-data-sync Specification

## Purpose

Ensures authenticated Tacticus synchronization detects and publishes player-content changes independently from changes to the game's configuration version.

## Requirements

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
The game catalog SHALL represent rewards for the same resource and battle as one farm location. When a battle awards one or more guaranteed or probabilistic copies of that resource simultaneously, the location's effective rate SHALL equal the number of guaranteed occurrences plus every probabilistic expected rate. A single probabilistic occurrence SHALL retain its chance ID, numerator, and denominator. Any consolidated location SHALL set all three fields to null because they cannot describe the combined yield. Every farm location SHALL additionally carry `expectedGold`: the average of its battle's guaranteed gold reward (`(min + max) / 2`), or null when that battle awards no guaranteed gold. `expectedGold` is a property of the battle, so every location sharing that battle (including two consolidated locations for two different resources dropped by the same battle) SHALL report the same value.

#### Scenario: Elite character shard has a guaranteed shard and bonus chance
- **GIVEN** an elite battle awards one guaranteed character shard and a `shard_elite` bonus with effective rate `0.079`
- **WHEN** character shard farm locations are denormalized
- **THEN** the catalog contains one location for that character and battle with `guaranteed` set, an effective rate of `1.079`, and null chance ID, numerator, and denominator

#### Scenario: A reward occurs only once in a battle
- **GIVEN** a battle has only one guaranteed or probabilistic occurrence of a resource
- **WHEN** farm locations are denormalized
- **THEN** its existing guaranteed and drop-chance semantics remain unchanged

#### Scenario: Two probabilistic occurrences use different chance definitions
- **GIVEN** a battle awards the same resource through two probabilistic occurrences with different chance IDs
- **WHEN** farm locations are denormalized
- **THEN** the catalog contains one location whose effective rate is the sum of both occurrences and whose chance ID, numerator, and denominator are null

#### Scenario: Two battles tie on efficiency but differ on gold
- **GIVEN** battle `FoCE13` guarantees gold `109`-`165` and battle `SHME19` guarantees gold `123`-`180`, and both battles also guarantee the same material
- **WHEN** farm locations for that material are denormalized
- **THEN** the `FoCE13` location reports `expectedGold` `137` and the `SHME19` location reports `expectedGold` `151.5`

#### Scenario: A battle guarantees no gold
- **GIVEN** a battle's guaranteed rewards contain no `gold` entry
- **WHEN** farm locations for that battle are denormalized
- **THEN** every location built from that battle reports `expectedGold` as null

### Requirement: Battle-attempt records preserve the campaign type they were reported under

Each synced battle-attempt record SHALL carry the campaign `type` (e.g. `Standard`, `Mirror`, `Elite`, `EliteMirror`, or, for a campaign event, `Standard`/`Extremis`) of the upstream campaign-progress entry it was derived from — the same `type` value already recorded on that entry's corresponding `campaign-progress` or `campaign-events-progress` record.

This applies uniformly to every campaign, not only campaign events: a standing campaign's battle-attempt records also carry their `type`, even though a standing campaign's `campaignId` alone already determines its type today.

#### Scenario: A campaign event reports two tiers in the same sync

- **GIVEN** an upstream synchronization response contains two campaign-progress entries sharing one campaign id — one with `type` `Standard`, one with `type` `Extremis` — each with its own `battles[]`
- **WHEN** battle-attempt records are derived from that response
- **THEN** every battle-attempt record derived from the `Standard` entry carries `type: "Standard"` and every one derived from the `Extremis` entry carries `type: "Extremis"`, so records from the two tiers remain distinguishable after being combined into one list

#### Scenario: A standing campaign's battle attempts also carry their type

- **GIVEN** an upstream synchronization response contains a standing (non-event) campaign's progress entry with `type` `Elite`
- **WHEN** battle-attempt records are derived from that entry
- **THEN** each derived battle-attempt record carries `type: "Elite"`

### Requirement: Served campaign battles carry their Tacticus battle index

Every battle in the served campaign-battles dataset SHALL carry `battleIndex`: the zero-based index Tacticus's own campaign-progress payloads use to identify that battle, assigned independently within each `{campaignGroupId, type}` track in that track's canonical order. This lets a consumer resolve a synced battle-attempt record (`campaignId`, `type`, `battleIndex`) to a specific served battle for any campaign, including an event campaign whose challenge nodes share their preceding node's `nodeNumber` and so cannot be distinguished by `nodeNumber` alone.

#### Scenario: A standing campaign's battles index sequentially

- **GIVEN** a standing campaign group's battles, ordered by node number, with no challenge nodes
- **WHEN** the campaign-battles dataset is served
- **THEN** each battle's `battleIndex` equals its position in that order, starting at 0

#### Scenario: An event campaign's challenge node has its own battle index despite sharing a node number

- **GIVEN** an event campaign's Standard track where node 3's challenge battle is interleaved after node 3's regular battle, both carrying `nodeNumber` 3
- **WHEN** the campaign-battles dataset is served
- **THEN** the regular node-3 battle and its challenge battle carry two different, sequential `battleIndex` values within that track — not the same value and not derived from `nodeNumber`

#### Scenario: Each event tier indexes independently

- **GIVEN** an event campaign group with a Standard track and an Extremis track sharing one campaign group id
- **WHEN** the campaign-battles dataset is served
- **THEN** the Standard track's battles and the Extremis track's battles are each indexed starting at 0 within their own `type`, independently of the other track
