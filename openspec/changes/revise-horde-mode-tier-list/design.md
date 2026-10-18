## Context

`horde-mode-tiers` is one curated record served as-is apart from sorting `characters` by `unitId` (`HordeModeTiersDenormalizer`). The validator pins the tier names to S, A, B, C. The client (`horde-tier-list.ts`) reads tiers in catalog order and already handles any tier name, so only its tier colour map needs a D entry (companion apps change).

## Goals / Non-Goals

**Goals:** add tier D; replace the seed list with the revised 47 characters.

**Non-Goals:** strategy text, relic-conditional multipliers, validating per-tier multiplier ranges, changing how clear estimates use multipliers.

## Decisions

- **D is ×1.00, same as untiered.** D means "evaluated, weak in horde modes"; untiered means "not evaluated". Neither penalises.
- **Every character carries its own `multiplier`.** The tier default is then only a fallback for future entries without one.
- **Ranges are not validated.** The (0, 5] bound stays. Enforcing per-tier ranges would block deliberate outliers for little gain.
- **Raw dataset change only.** No denormalizer change; the served shape is unchanged, so `SchemaVersion` stays and only `Version` bumps. The manifest hash for `horde-mode-tiers` changes and the snapshot test is re-verified.
- **No EF Core migration.**

## Revised list

| Tier (default) | Character | unitId | Multiplier | Was |
| --- | --- | --- | --- | --- |
| S (1.90) | Kariyan | custoBladeChampion | 2.00 | S 2.00 |
| | Isabella | adeptHospitaller | 1.98 | S 1.95 |
| | Nauseous | deathRotbone | 1.96 | A 1.45 |
| | Aleph-Null | necroSpyder | 1.94 | S 1.80 |
| | Re'vas | tauCrisis | 1.92 | A 1.40 |
| | Maladus | deathBlightlord | 1.90 | C 1.10 |
| | Tyrant Guard | tyranTyrantGuard | 1.88 | new |
| | Tan Gi'da | admecMarshall | 1.86 | S 1.75 |
| | Snotflogga | orksRuntherd | 1.83 | new |
| | Jaeger | templChampion | 1.81 | new |
| A (1.70) | Gulgortz | orksWarboss | 1.79 | B 1.30 |
| | Angrax | blackTerminator | 1.76 | C 1.05 |
| | Sho'syl | tauMarksman | 1.75 | S 1.90 |
| | Bellator | ultraInceptorSgt | 1.74 | S 1.85 |
| | Calgar | ultraCalgar | 1.72 | A 1.65 |
| | Actus | admecManipulus | 1.71 | new |
| | Tyrith | custoKyrus | 1.70 | new |
| | Nicodemus | bloodSanguinary | 1.69 | new |
| | Burchard | templAggressor | 1.68 | B 1.20 |
| | Wrask | worldTerminator | 1.67 | A 1.55 |
| | Anuphet | necroOverlord | 1.66 | new |
| | Titus | ultraTitus | 1.65 | new |
| | Vindicta | adeptRetributor | 1.64 | A 1.60 |
| | Typhus | deathTyphus | 1.63 | new |
| | Abraxas | thousInfernalMaster | 1.62 | A 1.50 |
| | Cezare | bloodTerminator | 1.60 | new |
| B (1.45) | Gibbascrapz | orksBigMek | 1.59 | B 1.15 |
| | Archimatos | blackPossession | 1.57 | B 1.25 |
| | Aesoth | custoVexilusPraetor | 1.55 | new |
| | Incisus | ultraApothecary | 1.53 | new |
| | Imospekh | necroDestroyer | 1.51 | new |
| | Lucius | emperLucius | 1.49 | new |
| | Toth | thousTerminator | 1.47 | new |
| | Corrodius | deathBlightbringer | 1.45 | new |
| | Xybia | genesMagus | 1.43 | new |
| | Mataneo | bloodIntercessor | 1.42 | new |
| | Abaddon | blackAbaddon | 1.40 | new |
| | Eldryon | eldarFarseer | 1.39 | B 1.35 |
| | Farsight | tauFarsight | 1.37 | new |
| | Makhotep | necroWarden | 1.35 | new |
| C (1.20) | Thaddeus | astraOrdnance | 1.32 | new |
| | Haarken | blackHaarken | 1.27 | new |
| | Tigurius | ultraTigurius | 1.22 | C 1.00 |
| | Sibyll | astraPrimarisPsy | 1.16 | new |
| | Roswitha | adeptCanoness | 1.12 | new |
| D (1.00) | Thutmose | necroPlasmancer | 1.05 | new |
| | Certus | ultraEliminatorSgt | 1.00 | C 1.00 |

All 26 new unit ids resolve to catalog characters (checked against `Data/units`). With 119 catalog characters, 72 stay untiered.

## Risks / Trade-offs

- The values are estimates, not measured. Clear estimates for teams with re-rated characters move (for example Maladus ×1.10 → ×1.90). Mitigation: calibrate against real clears in a later revision.
