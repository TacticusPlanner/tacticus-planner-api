using System.Globalization;
using System.Text.Json;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file most global-values fields are read from.</summary>
    public const string DatamineGlobalValuesPath = "gameconfig/globalValues.json";

    /// <summary>Raw path of the datamine list of energy-shop product ids, in purchase order.</summary>
    public const string DatamineStaminaShopTiersPath = "gameconfig/shop/staminaShopTiers.json";

    /// <summary>
    /// Builds the single global-values record from the datamine <c>globalValues.json</c> and the energy shop ladder
    /// (<c>staminaShopTiers.json</c> product ids resolved in <c>products.json</c>). Every stamina and team-size value
    /// must be positive, the team sizes ordered, and both battle-fatigue chances 0-100; each tier's product must give
    /// <c>stamina:N</c> (and optionally <c>raidTicket:N</c>) for a gem cost.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is missing or out of range, or a tier is unmappable.</exception>
    public static IReadOnlyList<GameCatalogGlobalValues> ReadDatamineGlobalValues(
        DatamineGlobalValues values,
        IReadOnlyList<string> staminaShopTiers,
        IReadOnlyDictionary<string, JsonElement> products)
    {
        int Positive(int? value, string name) => value is > 0
            ? value.Value
            : throw Invalid($"'{name}' must be positive; found {Describe(value)}.");

        int Percent(int? value, string name) => value is >= 0 and <= 100
            ? value.Value
            : throw Invalid($"'{name}' must be 0-100; found {Describe(value)}.");

        var minUnits = Positive(values.MinUnitsNeededForBattle, "minUnitsNeededForBattle");
        var maxUnits = Positive(values.MaxUnitsPossibleForBattle, "maxUnitsPossibleForBattle");
        if (maxUnits < minUnits)
        {
            throw Invalid($"'maxUnitsPossibleForBattle' {maxUnits} is below 'minUnitsNeededForBattle' {minUnits}.");
        }

        var record = new GameCatalogGlobalValues(
            Positive(values.MaxStamina, "maxStamina"),
            Positive(values.StaminaRegenerationTime, "staminaRegenerationTime"),
            Positive(values.StaminaRegenerationAmount, "staminaRegenerationAmount"),
            Positive(values.WavesMaxStamina, "wavesMaxStamina"),
            Positive(values.WavesStaminaRegenerationTime, "wavesStaminaRegenerationTime"),
            Positive(values.WavesStaminaRegenerationAmount, "wavesStaminaRegenerationAmount"),
            Positive(values.CampaignLevelMaxStamina, "campaignLevelMaxStamina"),
            Positive(values.CampaignLevelStaminaRegenerationTime, "campaignLevelStaminaRegenerationTime"),
            Positive(values.CampaignLevelStaminaRegenerationAmount, "campaignLevelStaminaRegenerationAmount"),
            minUnits,
            maxUnits,
            values.TerrifyingBonus ?? throw Invalid("'terrifyingBonus' is missing."),
            Percent(values.BattleFatigueFleeChance, "battleFatigueFleeChance"),
            Percent(values.BattleFatigueFleeChanceOverkill, "battleFatigueFleeChanceOverkill"),
            values.TraitVariables is { Count: > 0 } traitVariables
                ? new SortedDictionary<string, JsonElement>(traitVariables, StringComparer.Ordinal)
                : throw Invalid("'traitVariables' is missing or empty."),
            staminaShopTiers.Count > 0
                ? staminaShopTiers.Select(productId => ReadStaminaShopTier(productId, products)).ToArray()
                : throw Invalid("the stamina shop has no tiers."));

        return [record];
    }

    private static GameCatalogStaminaShopTier ReadStaminaShopTier(
        string productId,
        IReadOnlyDictionary<string, JsonElement> products)
    {
        if (!products.TryGetValue(productId, out var product)
            || !product.TryGetProperty("rewards", out var rewards)
            || rewards.ValueKind != JsonValueKind.Array
            || !product.TryGetProperty("cost", out var cost)
            || cost.ValueKind != JsonValueKind.Object
            || !cost.TryGetProperty("type", out var costType)
            || costType.GetString() != "gems"
            || !cost.TryGetProperty("amount", out var costAmount)
            || !costAmount.TryGetInt32(out var gems)
            || gems <= 0)
        {
            throw Invalid($"stamina shop tier '{productId}' must be a products.json product with rewards and a gem cost.");
        }

        var amounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var reward in rewards.EnumerateArray().Select(reward => reward.GetString() ?? ""))
        {
            var parts = reward.Split(':');
            if (parts is not [var type and ("stamina" or "raidTicket"), var amountText]
                || !int.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
                || amount <= 0
                || !amounts.TryAdd(type, amount))
            {
                throw Invalid($"stamina shop tier '{productId}' has reward '{reward}'; only one stamina:N and one raidTicket:N are mapped.");
            }
        }

        return amounts.TryGetValue("stamina", out var stamina)
            ? new GameCatalogStaminaShopTier(productId, stamina, amounts.GetValueOrDefault("raidTicket"), gems)
            : throw Invalid($"stamina shop tier '{productId}' gives no stamina.");
    }

    private static InvalidOperationException Invalid(string message) =>
        new($"Dataset '{GameCatalogDatasets.GlobalValues}': {message}");

    private static string Describe(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "none";
}
