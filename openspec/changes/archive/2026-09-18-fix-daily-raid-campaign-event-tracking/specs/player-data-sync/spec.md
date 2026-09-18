## ADDED Requirements

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
