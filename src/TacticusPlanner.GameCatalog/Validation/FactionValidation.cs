using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateFactions(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateFactions(snapshot.FactionViews, snapshot.CharacterViews, snapshot.MowList, snapshot.NpcList, errors);

    private static void ValidateDamageTypes(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateDamageTypes(snapshot.DamageTypeViews, snapshot.CharacterViews, snapshot.NpcList, errors);

    /// <summary>
    /// Every character's, MoW's and NPC's faction is a served faction with the same alliance. NPCs of the
    /// faction-less loot bucket (<c>Objects</c>/<c>Neutral</c>) are skipped. Each failure is an
    /// <c>InvalidFaction</c> error naming the unit and its faction.
    /// </summary>
    internal static void ValidateFactions(
        IReadOnlyList<GameCatalogFaction> factions,
        IReadOnlyList<GameCatalogCharacterView> characters,
        IReadOnlyList<GameCatalogMow> mows,
        IReadOnlyList<GameCatalogNpc> npcs,
        List<GameCatalogValidationError> errors)
    {
        ValidateUniqueIds(GameCatalogDatasets.Factions, factions, faction => faction.Id, errors);
        var allianceByFaction = factions
            .GroupBy(faction => faction.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Alliance, StringComparer.Ordinal);

        var units = characters.Select(character => (Kind: "character", character.Id, character.Faction, character.Alliance))
            .Concat(mows.Select(mow => (Kind: "MoW", mow.Id, mow.Faction, mow.Alliance)))
            .Concat(npcs
                .Where(npc => npc.Kind != "object")
                .Select(npc => (Kind: "npc", npc.Id, Faction: npc.FactionId, npc.Alliance)));

        foreach (var (kind, id, faction, alliance) in units)
        {
            if (!allianceByFaction.TryGetValue(faction, out var factionAlliance))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.Factions, "InvalidFaction", $"{kind} '{id}' has faction '{faction}', which is not a served faction."));
            }
            else if (!string.Equals(alliance, factionAlliance, StringComparison.Ordinal))
            {
                errors.Add(new GameCatalogValidationError(
                    GameCatalogDatasets.Factions,
                    "InvalidFaction",
                    $"{kind} '{id}' has alliance '{alliance}', but its faction '{faction}' is {factionAlliance}."));
            }
        }
    }

    /// <summary>
    /// Every damage type a character or NPC deals (weapons and abilities) is a served damage type. Each failure is an
    /// <c>InvalidDamageType</c> error naming the damage type and the first unit dealing it.
    /// </summary>
    internal static void ValidateDamageTypes(
        IReadOnlyList<GameCatalogDamageType> damageTypes,
        IReadOnlyList<GameCatalogCharacterView> characters,
        IReadOnlyList<GameCatalogNpc> npcs,
        List<GameCatalogValidationError> errors)
    {
        ValidateUniqueIds(GameCatalogDatasets.DamageTypes, damageTypes, damageType => damageType.Id, errors);
        var known = damageTypes.Select(damageType => damageType.Id).ToHashSet(StringComparer.Ordinal);

        var dealt = characters
            .Select(character => (character.Id, Types: character.ActiveAbilityDamage
                .Concat(character.PassiveAbilityDamage)
                .Append(character.MeleeDamage)
                .Append(character.RangedDamage)))
            .Concat(npcs.Select(npc => (npc.Id, Types: npc.ActiveAbilityDamage
                .Concat(npc.PassiveAbilityDamage)
                .Append(npc.MeleeDamage)
                .Append(npc.RangedDamage))))
            .SelectMany(unit => unit.Types
                .Where(type => !string.IsNullOrEmpty(type) && !known.Contains(type))
                .Select(type => (Type: type!, UnitId: unit.Id)))
            .GroupBy(entry => entry.Type, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        errors.AddRange(dealt.Select(group => new GameCatalogValidationError(
            GameCatalogDatasets.DamageTypes,
            "InvalidDamageType",
            $"damage type '{group.Key}' (dealt by '{group.First().UnitId}') is not a served damage type.")));
    }
}
