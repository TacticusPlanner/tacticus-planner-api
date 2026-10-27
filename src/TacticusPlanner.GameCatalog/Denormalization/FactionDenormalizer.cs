using System.Globalization;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file the factions dataset is built from.</summary>
    public const string DatamineFactionsPath = "gameconfig/units/factions.json";

    /// <summary>Raw path of the datamine file the damage-types dataset is built from.</summary>
    public const string DatamineDamageProfilesPath = "gameconfig/units/damageProfiles.json";

    private static readonly string[] FactionAlliances = ["Imperial", "Chaos", "Xenos"];

    /// <summary>
    /// Reads the datamine <c>factions.json</c> (faction id -> alliance trait, unlock time) into one record per faction,
    /// ordered by unlock time, then id. Each faction's <c>traits</c> must be exactly one alliance.
    /// </summary>
    /// <exception cref="InvalidOperationException">A faction has no single alliance or no unlock time.</exception>
    public static IReadOnlyList<GameCatalogFaction> ReadDatamineFactions(
        IReadOnlyDictionary<string, DatamineFaction> rows) =>
        rows
            .Select(pair =>
            {
                var (id, row) = pair;
                if (row.Traits is not [var alliance] || !FactionAlliances.Contains(alliance) || row.UnlockTimestamp <= 0)
                {
                    throw new InvalidOperationException(
                        $"Dataset '{GameCatalogDatasets.Factions}' faction '{id}' must have exactly one alliance trait " +
                        $"({string.Join(", ", FactionAlliances)}) and an unlockTimestamp; " +
                        $"found traits [{string.Join(", ", row.Traits ?? [])}] and unlockTimestamp {row.UnlockTimestamp}.");
                }

                var unlockDate = DateTimeOffset.FromUnixTimeMilliseconds(row.UnlockTimestamp)
                    .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                return (row.UnlockTimestamp, Faction: new GameCatalogFaction(id, alliance, unlockDate, row.CollectionFaction ?? false));
            })
            .OrderBy(entry => entry.UnlockTimestamp)
            .ThenBy(entry => entry.Faction.Id, StringComparer.Ordinal)
            .Select(entry => entry.Faction)
            .ToArray();

    /// <summary>
    /// Reads the datamine <c>damageProfiles.json</c> (damage type -> piercing ratio, traits) into one record per damage
    /// type, ordered by id. Absent traits serve as an empty list.
    /// </summary>
    /// <exception cref="InvalidOperationException">A damage type has no piercing ratio or one outside 0-100.</exception>
    public static IReadOnlyList<GameCatalogDamageType> ReadDatamineDamageTypes(
        IReadOnlyDictionary<string, DatamineDamageProfile> rows) =>
        rows
            .Select(pair =>
            {
                var (id, row) = pair;
                if (row.PiercingRatio is not { } piercingRatio || piercingRatio is < 0 or > 100)
                {
                    throw new InvalidOperationException(
                        $"Dataset '{GameCatalogDatasets.DamageTypes}' damage type '{id}' must have a PiercingRatio of 0-100; " +
                        $"found {row.PiercingRatio?.ToString(CultureInfo.InvariantCulture) ?? "none"}.");
                }

                return new GameCatalogDamageType(id, piercingRatio, row.Traits ?? []);
            })
            .OrderBy(damageType => damageType.Id, StringComparer.Ordinal)
            .ToArray();
}
