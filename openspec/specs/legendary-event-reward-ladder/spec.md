# legendary-event-reward-ladder Specification

## Purpose
Defines the reward ladder served for each Legendary Event (points milestones with their currency payouts, chest costs, shards per chest and the event unit's progression thresholds), how it is derived from the raw event file, and that no shared cross-event ladder dataset is served.

## Requirements

### Requirement: Each served Legendary Event carries its own reward ladder

Every record of the served `lres` dataset SHALL carry a `rewards` object with exactly these fields, read from the event's datamine sources:

```
rewards: {
  pointsMilestones: { milestone: int, cumulativePoints: int, engramPayout: int }[],  // ascending by cumulativePoints
  chestsMilestones: { chestLevel: int, engramCost: int }[],                          // ascending by chestLevel
  progression: { unlock: int, fourStars: int, fiveStars: int, blueStar: int, mythic: int, twoBlueStars: int },
  shardsPerChest: int
}
```

The client SHALL derive all reward arithmetic (currency totals, chests affordable, shard thresholds) from this object; the server sends no derived totals. `progression` values are the incremental shards for each step, not cumulative. The catalog loader SHALL reject an event whose `pointsMilestones` are not strictly ascending by `cumulativePoints` with `milestone` numbered 1..n, whose `chestsMilestones` are not numbered 1..n, or that has a non-positive payout, cost, progression step or `shardsPerChest`.

Assumptions:

- Ladders are a property of the event and may differ between events (today all four served events have the game's 60-chest ladder).
- The ladder is the same for all three runs of an event.

#### Scenario: Events with different chest ladders

- **WHEN** the client reads the `lres` dataset
- **THEN** each event's `rewards.chestsMilestones` is its own shop segment's ladder; today every served event has 60 entries summing to 44,450 currency

#### Scenario: Served values equal the raw file

- **GIVEN** the game gives `votanUthar` 25 event shards per chest and event-shard ascension costs 400, 120, 180, 200, 250 and 150
- **WHEN** the catalog is built
- **THEN** `votanUthar.rewards` has `shardsPerChest` 25 and `progression` `{ unlock 400, fourStars 120, fiveStars 180, blueStar 200, mythic 250, twoBlueStars 150 }`

#### Scenario: Malformed ladder

- **GIVEN** an event whose `chestsMilestones` skip from level 3 to level 5
- **WHEN** the catalog loads
- **THEN** loading fails with a validation error naming the event and the dataset

### Requirement: No shared reward ladder dataset is served

The catalog SHALL NOT serve an `lre-common` dataset: it SHALL be absent from the public manifest and its route SHALL respond 404. The manifest snapshot SHALL reflect the removal and the `lres` dataset's new content hash.

#### Scenario: Manifest no longer lists lre-common

- **WHEN** the client reads the public manifest
- **THEN** it contains `lres` and `lre-battles` and no `lre-common` entry
