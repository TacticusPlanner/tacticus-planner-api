namespace TacticusPlanner.GameCatalog.Models;

// One served faction (the factions dataset), read from the datamine's `units/factions.json`; see
// Denormalization/FactionDenormalizer.cs. Alliance is the faction's single alliance trait (Imperial, Chaos or Xenos);
// UnlockDateUtc is the game's unlockTimestamp as yyyy-MM-ddTHH:mm:ssZ; CollectionFaction marks a faction that groups
// units of other factions in the collection screen (Adeptus Astartes, The Lost and the Damned).
public sealed record GameCatalogFaction(
    string Id,
    string Alliance,
    string UnlockDateUtc,
    bool CollectionFaction
);

// One served damage type (the damage-types dataset), read from the datamine's `units/damageProfiles.json`.
// PiercingRatio is the game's percentage of a hit that ignores armour; Traits are the hit's extra effects
// (e.g. Flame -> OnFire), in game order.
public sealed record GameCatalogDamageType(
    string Id,
    int PiercingRatio,
    IReadOnlyList<string> Traits
);

// ---- datamine shapes -------------------------------------------------------------------------------------

// One value of Data/datamine/raw/gameconfig/units/factions.json, keyed by faction id. `unit` is always empty and is
// not bound.
public sealed record DatamineFaction(
    IReadOnlyList<string>? Traits,
    long UnlockTimestamp,
    bool? CollectionFaction
);

// One value of Data/datamine/raw/gameconfig/units/damageProfiles.json, keyed by damage type.
public sealed record DatamineDamageProfile(
    int? PiercingRatio,
    IReadOnlyList<string>? Traits
);
