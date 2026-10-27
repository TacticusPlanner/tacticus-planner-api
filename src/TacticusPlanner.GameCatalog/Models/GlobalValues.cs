using System.Text.Json;

namespace TacticusPlanner.GameCatalog.Models;

// The single served global-values record, read from the datamine's `globalValues.json` and `shop/staminaShopTiers.json`
// (resolved through `shop/products.json`); see Denormalization/GlobalValuesDenormalizer.cs. Field names are the
// game's. Times are seconds. Stamina is energy; waves stamina is Onslaught tokens; campaign-level stamina is the daily
// attempts of a limited (Elite) campaign battle.
public sealed record GameCatalogGlobalValues(
    int MaxStamina,
    int StaminaRegenerationTime,
    int StaminaRegenerationAmount,
    int WavesMaxStamina,
    int WavesStaminaRegenerationTime,
    int WavesStaminaRegenerationAmount,
    int CampaignLevelMaxStamina,
    int CampaignLevelStaminaRegenerationTime,
    int CampaignLevelStaminaRegenerationAmount,
    int MinUnitsNeededForBattle,
    int MaxUnitsPossibleForBattle,
    int TerrifyingBonus,
    int BattleFatigueFleeChance,
    int BattleFatigueFleeChanceOverkill,
    // Trait constants by game key (numbers, plus a few strings such as putridExplosionFactionId), as the game ships them.
    IReadOnlyDictionary<string, JsonElement> TraitVariables,
    IReadOnlyList<GameCatalogStaminaShopTier> StaminaShopTiers
);

// One energy purchase of the shop's ladder, in game order: the energy and raid tickets it gives and its gem cost.
public sealed record GameCatalogStaminaShopTier(
    string ProductId,
    int Stamina,
    int RaidTickets,
    int Gems
);

// ---- datamine shapes -------------------------------------------------------------------------------------

// The fields of Data/datamine/raw/gameconfig/globalValues.json the catalog reads; the file holds ~200 more.
public sealed record DatamineGlobalValues(
    int? MaxStamina,
    int? StaminaRegenerationTime,
    int? StaminaRegenerationAmount,
    int? WavesMaxStamina,
    int? WavesStaminaRegenerationTime,
    int? WavesStaminaRegenerationAmount,
    int? CampaignLevelMaxStamina,
    int? CampaignLevelStaminaRegenerationTime,
    int? CampaignLevelStaminaRegenerationAmount,
    int? MinUnitsNeededForBattle,
    int? MaxUnitsPossibleForBattle,
    int? TerrifyingBonus,
    int? BattleFatigueFleeChance,
    int? BattleFatigueFleeChanceOverkill,
    Dictionary<string, JsonElement>? TraitVariables
);
