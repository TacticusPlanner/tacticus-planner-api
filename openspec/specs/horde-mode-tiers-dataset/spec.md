# horde-mode-tiers-dataset Specification

## Purpose
Defines the served `horde-mode-tiers` catalog dataset: a curated S, A, B, C tier list of how well characters do in horde-style modes (many waves of weak enemies), with a power multiplier per tier and optional per-character multiplier overrides, its defaults and validation.

## Requirements

### Requirement: The catalog serves one horde-mode tier list

The catalog SHALL serve a dataset `horde-mode-tiers` containing exactly one record with these fields:

```
{
  id: "horde-mode-tiers",
  tiers: { tier: string, multiplier: number }[],   // exactly "S", "A", "B", "C" in that order; multiplier in (0, 5]
  characters: { unitId: string, tier: string,      // ascending by unitId
                multiplier?: number }[]           // optional override in (0, 5]; omitted when not set
}
```

The served record SHALL equal the raw `horde-mode-tiers.json` except that `characters` is sorted by `unitId` (ordinal) and a missing list is served empty. The server SHALL NOT expand defaults: a character's effective multiplier is its own `multiplier` when set, else its tier's multiplier; a character absent from `characters` is untiered, with multiplier 1.0. The client applies these rules. The dataset SHALL be listed in the public manifest with its content hash. No display strings are served.

Assumptions:

- One tier (and optional override) per character applies to every horde-style mode and, for the Legendary Event clear estimate, every lane and battle.

#### Scenario: Initial dataset

- **WHEN** the client reads `horde-mode-tiers`
- **THEN** it receives one record with tiers S, A, B, C in that order and the seed characters, including `custoBladeChampion` in S with multiplier 2.0 and `ultraEliminatorSgt` in C with multiplier 1.0

#### Scenario: Characters are sorted

- **GIVEN** the raw file lists `ultraTigurius` (S) before `bloodDante` (C)
- **WHEN** the catalog is built
- **THEN** the served `characters` lists `bloodDante` then `ultraTigurius`, and the hash is the same as for the reverse authoring order

#### Scenario: Override replaces the tier multiplier

- **GIVEN** tier S has multiplier 1.3 and the raw file lists `ultraTigurius` in S with `multiplier` 1.45 and `bloodDante` in S without one
- **WHEN** the client reads `horde-mode-tiers`
- **THEN** `ultraTigurius` carries `multiplier` 1.45, `bloodDante` has no `multiplier` field, and the client uses 1.45 and 1.3 respectively

#### Scenario: Listed in the manifest

- **WHEN** the client reads the public manifest
- **THEN** it contains a `horde-mode-tiers` entry with a content hash

### Requirement: The horde-mode tier list is validated on load

The catalog loader SHALL reject the raw file, with a validation error naming the dataset and the offending value, when: it does not contain exactly one record; the id is not `horde-mode-tiers`; `tiers` is not exactly S, A, B, C in that order; a multiplier is not in (0, 5]; a `unitId` is not a catalog character id; a `unitId` appears twice; a character's `tier` is not one of the tier names; or a character's `multiplier` is set and not in (0, 5].

#### Scenario: Unknown unit

- **GIVEN** the raw file lists `notAUnit` in tier A
- **WHEN** the catalog loads
- **THEN** loading fails with an error naming `horde-mode-tiers` and `notAUnit`

#### Scenario: Unknown tier

- **GIVEN** the raw file lists `ultraCalgar` in tier D
- **WHEN** the catalog loads
- **THEN** loading fails naming `ultraCalgar` and `D`

#### Scenario: Multiplier out of bounds

- **GIVEN** tier S has multiplier 0
- **WHEN** the catalog loads
- **THEN** loading fails naming tier `S` and the multiplier

#### Scenario: Override out of bounds

- **GIVEN** the raw file lists `ultraTigurius` in tier S with `multiplier` 0
- **WHEN** the catalog loads
- **THEN** loading fails naming `ultraTigurius` and the multiplier
