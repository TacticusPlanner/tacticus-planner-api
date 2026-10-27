using System.Globalization;
using System.Text.RegularExpressions;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

internal static partial class GameCatalogDenormalizer
{
    /// <summary>Raw path of the datamine file the mythic-quests dataset is built from.</summary>
    public const string DatamineQuestGroupsPath = "gameconfig/quests/groups.json";

    private const string MythicQuestPrefix = "mythic_";

    [GeneratedRegex(@"^mythic_(\w+)_(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex MythicQuestNameRegex();

    /// <summary>
    /// Reads the mythic quests out of the datamine <c>hero</c> quest group: every quest named
    /// <c>mythic_{characterId}_{number}</c>, grouped by character in game order.
    /// </summary>
    /// <exception cref="InvalidOperationException">A <c>mythic_</c> quest name has no character id or number.</exception>
    public static IReadOnlyList<GameCatalogRawMythicQuests> ReadDatamineMythicQuests(DatamineQuestGroup hero) =>
        hero.Quests
            .Where(quest => quest.Name.StartsWith(MythicQuestPrefix, StringComparison.Ordinal))
            .Select(quest =>
            {
                var match = MythicQuestNameRegex().Match(quest.Name);
                if (!match.Success)
                {
                    throw new InvalidOperationException(
                        $"Dataset '{GameCatalogDatasets.MythicQuests}' quest '{quest.Name}' is not named mythic_{{characterId}}_{{number}}.");
                }

                var number = int.Parse(match.Groups[2].ValueSpan, CultureInfo.InvariantCulture);
                return (CharacterId: match.Groups[1].Value, Quest: new GameCatalogRawMythicQuest(number, quest.Rewards, quest.Tasks));
            })
            .GroupBy(entry => entry.CharacterId, StringComparer.Ordinal)
            .Select(group => new GameCatalogRawMythicQuests(group.Key, group.Select(entry => entry.Quest).ToArray()))
            .ToArray();

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
/// Parses the game's mythic-quest reward strings (<c>"type[:amount]"</c>) into structured records. The set of
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
