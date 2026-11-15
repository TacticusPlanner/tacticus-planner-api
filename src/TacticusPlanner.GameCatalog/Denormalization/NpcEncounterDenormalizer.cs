using System.Globalization;
using System.Text.Json;

using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Denormalization;

/// <summary>The served views and raw waves the npc-encounters dataset is inverted from.</summary>
internal sealed record NpcEncounterSourceData(
    IReadOnlyList<GameCatalogNpc> Npcs,
    // Served character ids: a guild raid prime built on a hero names one as its questUnitId and is skipped.
    IReadOnlySet<string> CharacterIds,
    IReadOnlyList<GameCatalogCampaignBattleView> CampaignBattles,
    IReadOnlyList<GameCatalogCampaignDefinitionView> CampaignDefinitions,
    IReadOnlyList<GameCatalogLreBattleView> LreBattles,
    IReadOnlyList<GameCatalogQuestEventView> QuestEvents,
    IReadOnlyList<GameCatalogIncursion> Incursions,
    GameCatalogRaidBossesView RaidBosses,
    IReadOnlyDictionary<string, GameCatalogRaidBossRawUnitSet> RaidBossUnitSets,
    IReadOnlyList<GameCatalogRawSurvivalSeason> SurvivalSeasons,
    IReadOnlyList<DatamineOnslaughtWave> OnslaughtWaves)
{
    /// <summary>Each served npc's number of stat rows, which levels are resolved against.</summary>
    public IReadOnlyDictionary<string, int> LadderLengths { get; } =
        Npcs.ToDictionary(npc => npc.Id, npc => npc.Stats.Count, StringComparer.Ordinal);
}

internal static partial class GameCatalogDenormalizer
{
    /// <summary>The battleSets.json key prefix of a Survival season.</summary>
    public const string SurvivalBattleSetPrefix = "survival_";

    private const string CampaignEventReleaseType = "event";

    /// <summary>
    /// Reads every <c>survival_*</c> battle set of the datamine <c>battleSets.json</c> into its waves (the waves of
    /// all its battles, in order), ordered by set key.
    /// </summary>
    public static IReadOnlyList<GameCatalogRawSurvivalSeason> ReadDatamineSurvivalSeasons(
        IReadOnlyDictionary<string, JsonElement> battleSets, JsonSerializerOptions options) =>
        battleSets
            .Where(pair => pair.Key.StartsWith(SurvivalBattleSetPrefix, StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var battles = pair.Value.ValueKind == JsonValueKind.Array
                    ? pair.Value.Deserialize<List<DatamineBattleSetBattle>>(options) ?? []
                    : throw NpcEncounterError(NpcEncounterSources.Survival, $"battle set '{pair.Key}' is not a list of battles");
                return new GameCatalogRawSurvivalSeason(
                    pair.Key,
                    battles.SelectMany(battle => battle.Waves ?? []).ToArray());
            })
            .ToArray();

    /// <summary>
    /// Inverts the battle data into one record per (npc, source, group) — see the npc-encounters-dataset spec. Only
    /// <c>unit</c> and <c>machineOfWar</c> npcs are indexed; an enemy id that is not a served npc fails the load.
    /// </summary>
    public static IReadOnlyList<GameCatalogNpcEncounter> BuildNpcEncounters(NpcEncounterSourceData data)
    {
        var npcKinds = data.Npcs.ToDictionary(npc => npc.Id, npc => npc.Kind, StringComparer.Ordinal);
        var records = new Dictionary<(string NpcId, string Source, string GroupId), List<GameCatalogNpcEncounterBattle>>();

        void Add(string source, string npcId, string groupId, GameCatalogNpcEncounterBattle battle)
        {
            if (!npcKinds.TryGetValue(npcId, out var kind))
            {
                throw NpcEncounterError(source, $"group '{groupId}' names enemy '{npcId}', which is not a served npc");
            }

            if (kind == NpcKindObject)
            {
                return;
            }

            var key = (npcId, source, groupId);
            if (!records.TryGetValue(key, out var battles))
            {
                records[key] = battles = [];
            }

            if (!battles.Any(existing => SameBattle(existing, battle)))
            {
                battles.Add(battle);
            }
        }

        AddCampaignEncounters(data, Add);
        AddLegendaryEventEncounters(data, Add);
        AddQuestEventEncounters(data, Add);
        AddIncursionEncounters(data, Add);
        AddGuildRaidEncounters(data, Add);
        AddSurvivalEncounters(data, Add);
        AddOnslaughtEncounters(data, Add);

        var sourceOrder = NpcEncounterSources.Ordered
            .Select((source, index) => (source, index))
            .ToDictionary(pair => pair.source, pair => pair.index, StringComparer.Ordinal);

        return records
            .OrderBy(pair => pair.Key.NpcId, StringComparer.Ordinal)
            .ThenBy(pair => sourceOrder[pair.Key.Source])
            .ThenBy(pair => pair.Key.GroupId, StringComparer.Ordinal)
            .Select(pair => new GameCatalogNpcEncounter(pair.Key.NpcId, pair.Key.Source, pair.Key.GroupId, pair.Value.ToArray()))
            .ToArray();
    }

    private delegate void AddEncounter(string source, string npcId, string groupId, GameCatalogNpcEncounterBattle battle);

    private static void AddCampaignEncounters(NpcEncounterSourceData data, AddEncounter add)
    {
        var releaseTypes = data.CampaignDefinitions.ToDictionary(
            definition => definition.GroupId, definition => definition.ReleaseType, StringComparer.Ordinal);

        foreach (var battle in data.CampaignBattles)
        {
            var isEvent = releaseTypes.TryGetValue(battle.CampaignGroupId, out var releaseType)
                ? releaseType == CampaignEventReleaseType
                : throw NpcEncounterError(
                    NpcEncounterSources.Campaign,
                    $"battle '{battle.Id}' is in group '{battle.CampaignGroupId}', which has no campaign definition");
            var source = isEvent ? NpcEncounterSources.CampaignEvent : NpcEncounterSources.Campaign;
            foreach (var enemies in battle.DetailedEnemyTypes.GroupBy(enemy => enemy.Id, StringComparer.Ordinal))
            {
                add(source, enemies.Key, battle.CampaignGroupId, new GameCatalogNpcEncounterBattle(
                    battle.Id, isEvent ? battle.Type : null, null, battle.NodeNumber, battle.Challenge, NpcEncounterRoles.Enemy, [],
                    enemies.Sum(enemy => enemy.Count),
                    EncounterLevels(enemies.Select(enemy => ((int?)enemy.ServedIndex, enemy.Count)))));
            }
        }
    }

    private static void AddLegendaryEventEncounters(NpcEncounterSourceData data, AddEncounter add)
    {
        foreach (var battle in data.LreBattles)
        {
            foreach (var enemies in battle.Waves
                         .SelectMany(wave => wave.Enemies)
                         .GroupBy(enemy => enemy.Id, StringComparer.Ordinal))
            {
                add(NpcEncounterSources.LegendaryEvent, enemies.Key, battle.LreId, new GameCatalogNpcEncounterBattle(
                    battle.Id, battle.Track, null, battle.Number, false, NpcEncounterRoles.Enemy, [],
                    enemies.Sum(enemy => enemy.Count),
                    EncounterLevels(enemies.Select(enemy => (LadderRow(data, enemy.Id, enemy.LadderIndex), enemy.Count)))));
            }
        }
    }

    private static void AddQuestEventEncounters(NpcEncounterSourceData data, AddEncounter add)
    {
        foreach (var quest in data.QuestEvents)
        {
            foreach (var battle in quest.Battles)
            {
                foreach (var enemies in battle.Enemies.GroupBy(enemy => enemy.Id, StringComparer.Ordinal))
                {
                    add(NpcEncounterSources.QuestEvent, enemies.Key, quest.FeaturedCharacterId, new GameCatalogNpcEncounterBattle(
                        null, null, null, battle.Number, false, NpcEncounterRoles.Enemy, [],
                        enemies.Sum(enemy => enemy.Count),
                        EncounterLevels(enemies.Select(enemy => (LadderRow(data, enemy.Id, enemy.LadderIndex), enemy.Count)))));
                }
            }
        }
    }

    private static void AddIncursionEncounters(NpcEncounterSourceData data, AddEncounter add)
    {
        foreach (var incursion in data.Incursions)
        {
            foreach (var tier in incursion.Tiers)
            {
                foreach (var battle in tier.Battles)
                {
                    foreach (var enemies in battle.Enemies.GroupBy(enemy => enemy.NpcId, StringComparer.Ordinal))
                    {
                        add(NpcEncounterSources.Incursion, enemies.Key, incursion.MowId, new GameCatalogNpcEncounterBattle(
                            battle.Id, null, tier.Index, null, battle.Challenge, NpcEncounterRoles.Enemy, [],
                            enemies.Sum(enemy => enemy.Count),
                            EncounterLevels(enemies.Select(enemy => (CappedLadderRow(data, enemy.NpcId, enemy.Level), enemy.Count)))));
                    }
                }
            }
        }
    }

    private static void AddGuildRaidEncounters(NpcEncounterSourceData data, AddEncounter add)
    {
        // The boss / prime itself, when its unit set names the npc it represents (a hero-based prime names a character
        // instead, which is not an npc).
        foreach (var unit in data.RaidBosses.Bosses.Concat(data.RaidBosses.Primes))
        {
            if (unit.QuestUnitId is { } npcId && !data.CharacterIds.Contains(npcId))
            {
                var role = unit.Kind == "prime" ? NpcEncounterRoles.Prime : NpcEncounterRoles.Boss;
                add(NpcEncounterSources.GuildRaid, npcId, unit.UnitSetId,
                    new GameCatalogNpcEncounterBattle(null, null, null, null, false, role, [], null, []));
            }
        }

        // The field units of each encounter, grouped under the boss / prime the encounter fights.
        var fieldEntry = new GameCatalogNpcEncounterBattle(null, null, null, null, false, NpcEncounterRoles.Enemy, [], null, []);
        foreach (var encounter in data.RaidBosses.Seasons.Values
                     .SelectMany(season => season.Tiers)
                     .SelectMany(tier => tier.Sets)
                     .SelectMany(set => set.Encounters))
        {
            foreach (var fieldUnitSetId in encounter.FieldNpcIds)
            {
                if (data.RaidBossUnitSets.TryGetValue(fieldUnitSetId, out var unitSet)
                    && unitSet.QuestUnitId is { } npcId
                    && !data.CharacterIds.Contains(npcId))
                {
                    add(NpcEncounterSources.GuildRaid, npcId, encounter.UnitSetId, fieldEntry);
                }
            }
        }
    }

    private static void AddSurvivalEncounters(NpcEncounterSourceData data, AddEncounter add)
    {
        foreach (var season in data.SurvivalSeasons)
        {
            var spawns = season.Waves.SelectMany(wave => (wave.Army ?? [])
                .Select(entry => (wave.Round, Enemy: ReadEnemy(NpcEncounterSources.Survival, season.Id, entry))));

            foreach (var npc in spawns.GroupBy(spawn => spawn.Enemy.NpcId, StringComparer.Ordinal))
            {
                foreach (var round in npc.GroupBy(spawn => spawn.Round).OrderBy(round => round.Key))
                {
                    add(NpcEncounterSources.Survival, npc.Key, season.Id, new GameCatalogNpcEncounterBattle(
                        null, null, null, null, false, NpcEncounterRoles.Enemy, [round.Key], round.Count(),
                        EncounterLevels(round.Select(spawn => (LadderRow(data, npc.Key, spawn.Enemy.Index), 1)))));
                }
            }
        }
    }

    private static void AddOnslaughtEncounters(NpcEncounterSourceData data, AddEncounter add)
    {
        foreach (var tier in data.OnslaughtWaves.GroupBy(wave => wave.Tier).OrderBy(tier => tier.Key))
        {
            var spawns = tier.SelectMany((wave, waveNumber) => (wave.Enemies ?? [])
                .Select(entry => (waveNumber, wave.Round, Enemy: ReadEnemy(NpcEncounterSources.Onslaught, $"tier {wave.Tier}", entry))));

            foreach (var npc in spawns.GroupBy(spawn => spawn.Enemy.NpcId, StringComparer.Ordinal))
            {
                // A tier's waves are alternatives, so counts take the largest wave rather than adding them up.
                add(NpcEncounterSources.Onslaught, npc.Key, NpcEncounterSources.OnslaughtGroupId, new GameCatalogNpcEncounterBattle(
                    null, null, tier.Key, null, false, NpcEncounterRoles.Enemy,
                    npc.Select(spawn => spawn.Round).Distinct().Order().ToArray(),
                    npc.GroupBy(spawn => spawn.waveNumber).Max(wave => wave.Count()),
                    EncounterLevels(npc
                        .GroupBy(spawn => LadderRow(data, npc.Key, spawn.Enemy.Index))
                        .Select(level => (level.Key, level.GroupBy(spawn => spawn.waveNumber).Max(wave => wave.Count()))))));
            }
        }
    }

    /// <summary>One <c>(level, count)</c> per level, added up and lowest first; an unknown level comes last.</summary>
    private static GameCatalogNpcEncounterLevel[] EncounterLevels(IEnumerable<(int? ServedIndex, int Count)> levels) =>
        levels
            .GroupBy(level => level.ServedIndex)
            .Select(level => new GameCatalogNpcEncounterLevel(level.Key, level.Sum(entry => entry.Count)))
            .OrderBy(level => level.ServedIndex ?? int.MaxValue)
            .ToArray();

    /// <summary>The stat row of a 1-based <c>{npcId}:{N}</c> index; null past the ladder.</summary>
    private static int? LadderRow(NpcEncounterSourceData data, string npcId, int index)
    {
        var row = Math.Max(0, index - 1);
        return data.LadderLengths.TryGetValue(npcId, out var length) && row < length ? row : null;
    }

    /// <summary>The stat row of a 1-based Incursion level, which caps at the last row.</summary>
    private static int? CappedLadderRow(NpcEncounterSourceData data, string npcId, int level) =>
        data.LadderLengths.TryGetValue(npcId, out var length) && length > 0
            ? Math.Clamp(level - 1, 0, length - 1)
            : null;

    /// <summary>A raw <c>"{npcId}:{N}"</c> wave entry; anything else fails the load.</summary>
    private static (string NpcId, int Index) ReadEnemy(string source, string where, string entry)
    {
        var separator = entry.LastIndexOf(':');
        return separator > 0 && int.TryParse(entry.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? (entry[..separator], index)
            : throw NpcEncounterError(source, $"{where} has enemy entry '{entry}', which is not \"npcId:N\"");
    }

    private static bool SameBattle(GameCatalogNpcEncounterBattle left, GameCatalogNpcEncounterBattle right) =>
        left.BattleId == right.BattleId
        && left.Track == right.Track
        && left.Tier == right.Tier
        && left.Number == right.Number
        && left.Challenge == right.Challenge
        && left.Role == right.Role
        && left.Count == right.Count
        && left.Levels.SequenceEqual(right.Levels)
        && left.Rounds.SequenceEqual(right.Rounds);

    private static InvalidOperationException NpcEncounterError(string source, string detail) =>
        new($"Dataset '{GameCatalogDatasets.NpcEncounters}' ({source}): {detail}.");
}
