# legendary-event-reward-ladder Specification

## Purpose

Defines the reward ladder served for each Legendary Event (points milestones with their currency payouts, chest costs, shards per chest and the event unit's progression thresholds), how it is derived from the raw event file, and that no shared cross-event ladder dataset is served.

## ADDED Requirements

### Requirement: Each served Legendary Event carries its own reward ladder

Every record of the served `lres` dataset SHALL carry a `rewards` object with exactly these fields, copied from that event's raw `lres-*.json` file without transformation:

```
rewards: {
  pointsMilestones: { milestone: int, cumulativePoints: int, engramPayout: int }[],  // ascending by cumulativePoints
  chestsMilestones: { chestLevel: int, engramCost: int }[],                          // ascending by chestLevel
  progression: { unlock: int, fourStars: int, fiveStars: int, blueStar: int, mythic: int, twoBlueStars: int },
  shardsPerChest: int
}
```

The client SHALL derive all reward arithmetic (currency totals, chests affordable, shard thresholds) from this object; the server sends no derived totals. `progression` values are the incremental shards for each step, not cumulative. The catalog loader SHALL reject a raw file whose `pointsMilestones` are not strictly ascending by `cumulativePoints` with `milestone` numbered 1..n, whose `chestsMilestones` are not numbered 1..n, or that has a non-positive payout, cost, progression step or `shardsPerChest`.

Assumptions:

- Ladders are a property of the event and may differ between events (today the chest ladder does: 60 rungs for `astarLysander` and `lostAbile`, 54 for `tauFarsight` and `votanUthar`).
- The ladder is the same for all three runs of an event.

#### Scenario: Events with different chest ladders

- **WHEN** the client reads the `lres` dataset
- **THEN** `astarLysander.rewards.chestsMilestones` has 60 entries summing to 44,450 currency and `votanUthar.rewards.chestsMilestones` has 54 entries summing to 35,450, with identical first 54 costs

#### Scenario: Served values equal the raw file

- **GIVEN** `lres-votanuthar.json` has `shardsPerChest` 25 and `progression` `{ unlock 400, fourStars 120, fiveStars 180, blueStar 200, mythic 250, twoBlueStars 150 }`
- **WHEN** the catalog is built
- **THEN** `votanUthar.rewards` carries those values unchanged

#### Scenario: Malformed ladder

- **GIVEN** a raw event file whose `chestsMilestones` skip from level 3 to level 5
- **WHEN** the catalog loads
- **THEN** loading fails with a validation error naming the event and the dataset

### Requirement: No shared reward ladder dataset is served

The catalog SHALL NOT serve an `lre-common` dataset: it SHALL be absent from the public manifest and its route SHALL respond 404. The manifest snapshot SHALL reflect the removal and the `lres` dataset's new content hash.

#### Scenario: Manifest no longer lists lre-common

- **WHEN** the client reads the public manifest
- **THEN** it contains `lres` and `lre-battles` and no `lre-common` entry
