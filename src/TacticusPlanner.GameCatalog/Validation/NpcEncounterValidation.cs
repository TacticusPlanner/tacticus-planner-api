using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.GameCatalog.Validation;

public static partial class GameCatalogValidator
{
    private static void ValidateNpcEncounters(GameCatalogSnapshot snapshot, List<GameCatalogValidationError> errors) =>
        ValidateNpcEncounters(snapshot.NpcEncounterViews, snapshot, errors);

    /// <summary>
    /// Every encounter names a served npc, a known source, a group that exists in that source's served dataset and,
    /// where it carries one, a battle id that exists there; it lists at least one battle; and each battle counts its
    /// npc with a positive number and splits it by level, except a guild raid's, which has neither. Each failure is an
    /// <c>InvalidNpcEncounter</c> error. (An unknown enemy id fails earlier, in the build.)
    /// </summary>
    internal static void ValidateNpcEncounters(
        IReadOnlyList<GameCatalogNpcEncounter> encounters,
        GameCatalogSnapshot snapshot,
        List<GameCatalogValidationError> errors)
    {
        var npcIds = snapshot.NpcList.Select(npc => npc.Id).ToHashSet(StringComparer.Ordinal);
        var campaignBattles = snapshot.CampaignBattleViews.ToDictionary(battle => battle.Id, StringComparer.Ordinal);
        var lreBattles = snapshot.LreBattleViews.ToDictionary(battle => battle.Id, StringComparer.Ordinal);
        var questIds = snapshot.QuestEventViews.Select(quest => quest.FeaturedCharacterId).ToHashSet(StringComparer.Ordinal);
        var incursionBattles = snapshot.IncursionViews.ToDictionary(
            incursion => incursion.MowId,
            incursion => incursion.Tiers.SelectMany(tier => tier.Battles).Select(battle => battle.Id).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var raidBossIds = snapshot.RaidBossesView.Bosses.Concat(snapshot.RaidBossesView.Primes)
            .Select(unit => unit.UnitSetId)
            .ToHashSet(StringComparer.Ordinal);
        var sources = NpcEncounterSources.Ordered.ToHashSet(StringComparer.Ordinal);

        void Fail(GameCatalogNpcEncounter encounter, string message) =>
            errors.Add(new GameCatalogValidationError(
                GameCatalogDatasets.NpcEncounters,
                "InvalidNpcEncounter",
                $"Encounter '{encounter.NpcId}' / {encounter.Source} / '{encounter.GroupId}' {message}"));

        foreach (var encounter in encounters)
        {
            if (!npcIds.Contains(encounter.NpcId))
            {
                Fail(encounter, "names an npc that is not served.");
            }

            if (!sources.Contains(encounter.Source))
            {
                Fail(encounter, "has an unknown source.");
                continue;
            }

            if (encounter.Battles.Count == 0)
            {
                Fail(encounter, "lists no battle.");
            }

            var counted = encounter.Source != NpcEncounterSources.GuildRaid;
            if (encounter.Battles.Any(battle => counted ? battle.Count is null or < 1 : battle.Count is not null))
            {
                Fail(encounter, counted ? "has a battle without a positive count." : "counts a guild raid battle.");
            }

            if (encounter.Battles.Any(battle => counted
                    ? battle.Levels.Count == 0 || battle.Levels.Any(level => level.Count < 1 || level.ServedIndex < 0)
                    : battle.Levels.Count > 0))
            {
                Fail(encounter, counted ? "has a battle without valid levels." : "gives a guild raid battle levels.");
            }

            switch (encounter.Source)
            {
                case NpcEncounterSources.Campaign or NpcEncounterSources.CampaignEvent:
                    foreach (var battle in encounter.Battles)
                    {
                        if (battle.BattleId is null
                            || !campaignBattles.TryGetValue(battle.BattleId, out var served)
                            || served.CampaignGroupId != encounter.GroupId)
                        {
                            Fail(encounter, $"names battle '{battle.BattleId}', which is not a served battle of that campaign.");
                        }
                    }

                    break;
                case NpcEncounterSources.LegendaryEvent:
                    foreach (var battle in encounter.Battles)
                    {
                        if (battle.BattleId is null
                            || !lreBattles.TryGetValue(battle.BattleId, out var served)
                            || served.LreId != encounter.GroupId)
                        {
                            Fail(encounter, $"names battle '{battle.BattleId}', which is not a served battle of that event.");
                        }
                    }

                    break;
                case NpcEncounterSources.QuestEvent when !questIds.Contains(encounter.GroupId):
                    Fail(encounter, "is not a served quest.");
                    break;
                case NpcEncounterSources.Incursion:
                    if (!incursionBattles.TryGetValue(encounter.GroupId, out var battleIds))
                    {
                        Fail(encounter, "is not a served incursion.");
                        break;
                    }

                    foreach (var battle in encounter.Battles.Where(battle => battle.BattleId is null || !battleIds.Contains(battle.BattleId)))
                    {
                        Fail(encounter, $"names battle '{battle.BattleId}', which is not a battle of that incursion.");
                    }

                    break;
                case NpcEncounterSources.GuildRaid when !raidBossIds.Contains(encounter.GroupId):
                    Fail(encounter, "is not a served raid boss or prime.");
                    break;
            }
        }
    }
}
