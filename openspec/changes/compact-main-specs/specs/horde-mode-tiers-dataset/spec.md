## MODIFIED Requirements

### Requirement: The catalog serves one horde-mode tier list

The catalog SHALL serve a dataset `horde-mode-tiers` containing exactly one record with these fields:

```
{
  id: "horde-mode-tiers",
  tiers: { tier: string, multiplier: number }[],   // exactly "S", "A", "B", "C", "D" in that order; multiplier in (0, 5]
  characters: { unitId: string, tier: string,      // ascending by unitId
                multiplier?: number }[]           // optional override in (0, 5]; omitted when not set
}
```

The served record SHALL equal the raw `horde-mode-tiers.json` except that `characters` is sorted by `unitId` (ordinal) and a missing list is served empty. The server SHALL NOT expand defaults: a character's effective multiplier is its own `multiplier` when set, else its tier's multiplier; a character absent from `characters` is untiered, with multiplier 1.0. The client applies these rules. No display strings are served.

Tier D marks a character evaluated as weak in horde-style modes; untiered marks one not yet evaluated. Both may use 1.0.

Assumptions:

- One tier (and optional override) per character applies to every horde-style mode and, for the Legendary Event clear estimate, every lane and battle.
- The multipliers are curated balancing estimates, not measured clear rates.

#### Scenario: Initial dataset

- **WHEN** the client reads `horde-mode-tiers`
- **THEN** it receives one record with tiers S 1.90, A 1.70, B 1.45, C 1.20, D 1.00 in that order and 47 characters, including `custoBladeChampion` in S with multiplier 2.0, `deathBlightlord` in S with multiplier 1.9, `blackTerminator` in A with multiplier 1.76 and `ultraEliminatorSgt` in D with multiplier 1.0

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
