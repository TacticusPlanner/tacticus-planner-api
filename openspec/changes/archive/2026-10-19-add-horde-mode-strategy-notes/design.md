## Context

Served datasets carry ids, never display strings, so strengths, how-to-use and relic-advice text live in the client's locale files (companion apps change). Only the teammate list is structured.

## Decisions

- **`synergies` on the character entry**, served in authoring order (the order is a recommendation ranking), omitted when empty. The hash therefore depends on that order, which is intended.
- **Ids only, any catalog character.** A synergy may point to an untiered character. At most 6, to fit one row of portraits.
- **No EF Core migration.** `SchemaVersion` bumps (8 → 9) because the served shape gains a field; `Version` becomes `dev-2026-10-19`.
- **An empty list is served as not set**, so `[]` and a missing field hash the same.

## Seed synergies

Only characters whose notes name teammates get synergies, and only the teammates named; the rest have none. (Angrax's notes name Rotbone, Wrask and Abaddon, so Maladus, in an earlier draft, is left out.)

| Character | unitId | synergies |
| --- | --- | --- |
| Nauseous | deathRotbone | deathBlightlord, blackTerminator |
| Aleph-Null | necroSpyder | tauCrisis |
| Re'vas | tauCrisis | necroSpyder |
| Maladus | deathBlightlord | deathRotbone |
| Tan Gi'da | admecMarshall | admecManipulus |
| Angrax | blackTerminator | deathRotbone, worldTerminator, blackAbaddon |
| Actus | admecManipulus | admecMarshall, tauCrisis |
| Tyrith | custoKyrus | adeptHospitaller |
| Abaddon | blackAbaddon | blackTerminator, worldTerminator |
