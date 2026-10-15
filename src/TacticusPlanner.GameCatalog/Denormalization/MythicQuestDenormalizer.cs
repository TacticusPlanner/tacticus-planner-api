using System.Globalization;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    public static IReadOnlyList<GameCatalogMythicQuestsView> BuildMythicQuests(
        IReadOnlyList<GameCatalogRawMythicQuests> raw) =>
        raw
            .OrderBy(character => character.CharacterId, StringComparer.Ordinal)
            .Select(character => new GameCatalogMythicQuestsView(
                character.CharacterId,
                character.Quests
                    .OrderBy(quest => quest.Number)
                    .Select(quest => new GameCatalogMythicQuestView(
                        quest.Number,
                        quest.Tasks
                            .Select(task => new GameCatalogMythicQuestTaskView(task.LocaKey, task.Target, task.TaskParameters))
                            .ToArray(),
                        // An unparseable reward is reported by validation (startup fails); it is skipped here so
                        // the build itself never throws before validation can name it.
                        quest.Rewards
                            .Select(reward => MythicQuestRewardParser.TryParse(reward, out var view) ? view : null)
                            .OfType<GameCatalogMythicQuestRewardView>()
                            .ToArray()))
                    .ToArray()))
            .ToArray();
}

/// <summary>
/// Parses V1's mythic-quest reward strings (<c>"type[:amount]"</c>) into structured records. The set of
/// patterns is closed: any other string fails to parse, so a new reward kind in a game refresh surfaces as a
/// startup validation error rather than an unknown reward.
/// </summary>
internal static partial class MythicQuestRewardParser
{
    public const string MythicShards = "MythicShards";
    public const string AscensionOrb = "AscensionOrb";
    public const string AbilityBadge = "AbilityBadge";
    public const string XpBook = "XpBook";
    public const string Gold = "Gold";
    public const string Equipment = "Equipment";

    private const string Mythic = "Mythic";

    public static bool TryParse(string reward, out GameCatalogMythicQuestRewardView view)
    {
        view = null!;
        var colon = reward.IndexOf(':', StringComparison.Ordinal);
        var type = colon == -1 ? reward : reward[..colon];
        var amount = 1;
        if (colon != -1
            && (!int.TryParse(reward[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out amount) || amount < 1))
        {
            return false;
        }

        if (MythicShardsPattern().Match(type) is { Success: true } shards)
        {
            view = new GameCatalogMythicQuestRewardView(MythicShards, amount, UnitId: shards.Groups[1].Value);
        }
        else if (AscensionOrbPattern().Match(type) is { Success: true } orb)
        {
            view = new GameCatalogMythicQuestRewardView(AscensionOrb, amount, Rarity: Mythic, Alliance: orb.Groups[1].Value);
        }
        else if (AbilityBadgePattern().Match(type) is { Success: true } badge)
        {
            view = new GameCatalogMythicQuestRewardView(AbilityBadge, amount, Rarity: Mythic, Alliance: badge.Groups[1].Value);
        }
        else if (type == "xpMythic")
        {
            view = new GameCatalogMythicQuestRewardView(XpBook, amount, Rarity: Mythic);
        }
        else if (type == "gold")
        {
            view = new GameCatalogMythicQuestRewardView(Gold, amount);
        }
        else if (EquipmentPattern().IsMatch(type))
        {
            view = new GameCatalogMythicQuestRewardView(Equipment, amount, EquipmentId: type);
        }
        else
        {
            return false;
        }

        return true;
    }

    [GeneratedRegex("^mythicShards_([A-Za-z0-9]+)$")]
    private static partial Regex MythicShardsPattern();

    [GeneratedRegex("^heroAscensionOrbMythic_(Imperial|Chaos|Xenos)$")]
    private static partial Regex AscensionOrbPattern();

    [GeneratedRegex("^abilityTokenMythic_(Imperial|Chaos|Xenos)$")]
    private static partial Regex AbilityBadgePattern();

    [GeneratedRegex("^[IR]_[A-Za-z0-9_]+$")]
    private static partial Regex EquipmentPattern();
}
