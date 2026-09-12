using TacticusPlanner.Domain.GuildRaids;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Features.Guilds;

public static class GuildRaidStatusProjector
{
    public static GuildRaidStatusResponse Project(
        GuildRaidSyncState syncState,
        GuildRaidSeason? season,
        GameCatalogSnapshot catalog,
        DateTimeOffset lastGuildSyncSucceededAt,
        GuildRaidFreshness freshness)
    {
        var observedAt = syncState.ObservedAt
            ?? throw new InvalidOperationException("The Guild Raid sync state has no successful observation to project.");

        if (syncState.State == GuildRaidObservationState.NoActiveSeason || season is null)
        {
            return new GuildRaidStatusResponse(
                GuildRaidObservationState.NoActiveSeason,
                observedAt,
                freshness,
                lastGuildSyncSucceededAt,
                null);
        }

        if (!catalog.RaidBossesView.Seasons.TryGetValue(season.SeasonConfigId, out var config))
        {
            throw new InvalidOperationException($"Unknown Guild Raid season config '{season.SeasonConfigId}'.");
        }

        var positions = config.Tiers
            .SelectMany((tier, tierIndex) => tier.Sets.Select((set, setIndex) => new Position(tier, set, tierIndex, setIndex)))
            .ToArray();
        if (positions.Length == 0)
        {
            throw new InvalidOperationException($"Guild Raid season config '{season.SeasonConfigId}' has no sets.");
        }

        var bossAttacks = season.Attacks
            .Where(attack => attack.EncounterType == GuildRaidEncounterType.Boss)
            .OrderBy(hit => hit.CompletedAt)
            .ToArray();
        var latestBoss = bossAttacks.LastOrDefault();
        var currentIndex = latestBoss is null ? 0 : FindPositionIndex(positions, latestBoss);
        var upcoming = latestBoss is null || latestBoss.RemainingHp == 0;
        DateTimeOffset? observationCutoff = null;

        if (latestBoss is { RemainingHp: 0 })
        {
            observationCutoff = latestBoss.CompletedAt;
            currentIndex = NextPositionIndex(positions, currentIndex, season.SeasonConfigId, catalog);
        }
        else if (latestBoss is not null)
        {
            observationCutoff = bossAttacks.LastOrDefault(attack => attack.RemainingHp == 0)?.CompletedAt;
        }

        var position = positions[currentIndex];
        var bossEncounter = position.Set.Encounters.FirstOrDefault(encounter =>
            string.Equals(encounter.EncounterType, "Boss", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected Guild Raid set has no main boss encounter.");
        var bossCatalog = catalog.RaidBossesView.Bosses.FirstOrDefault(unit => unit.UnitSetId == bossEncounter.UnitSetId);
        var bossStep = StatStepAt(bossCatalog, bossEncounter.ProgressionIndex);
        var matchingBossHit = !upcoming && latestBoss is not null
            && Matches(latestBoss, position, bossEncounter)
                ? latestBoss
                : null;
        var bossMaximumHp = matchingBossHit?.MaximumHp > 0 ? matchingBossHit.MaximumHp : bossStep?.Health ?? 0;
        if (bossMaximumHp <= 0)
        {
            throw new InvalidOperationException("The current Guild Raid boss maximum HP cannot be resolved.");
        }

        var difficulty = bossStep is not null
            ? ParseDifficulty(bossStep.BaseRarity)
            : matchingBossHit?.Difficulty
                ?? throw new InvalidOperationException("The current Guild Raid difficulty cannot be resolved.");
        var boss = new GuildRaidBossStatusResponse(
            bossEncounter.UnitSetId,
            bossEncounter.ProgressionIndex,
            matchingBossHit?.RemainingHp ?? bossMaximumHp,
            bossMaximumHp,
            upcoming);
        var primes = position.Set.Encounters
            .Where(encounter => !string.Equals(encounter.EncounterType, "Boss", StringComparison.OrdinalIgnoreCase))
            .OrderBy(encounter => encounter.EncounterIndex)
            .Select(encounter => ProjectPrime(encounter, position, season.Attacks, observationCutoff, catalog))
            .ToArray();

        return new GuildRaidStatusResponse(
            GuildRaidObservationState.Active,
            observedAt,
            freshness,
            lastGuildSyncSucceededAt,
            new GuildRaidSeasonStatusResponse(
                season.SeasonNumber,
                season.SeasonConfigId,
                ResolveEndsAt(season, observedAt, catalog),
                position.TierIndex,
                position.SetIndex,
                position.Tier.Sets.Count,
                difficulty,
                boss,
                primes));
    }

    private static GuildRaidPrimeStatusResponse ProjectPrime(
        GameCatalogRaidBossEncounterView encounter,
        Position position,
        IEnumerable<GuildRaidAttack> attacks,
        DateTimeOffset? cutoff,
        GameCatalogSnapshot catalog)
    {
        var attack = attacks
            .Where(candidate => (cutoff is null || candidate.CompletedAt > cutoff)
                && Matches(candidate, position, encounter))
            .OrderBy(candidate => candidate.CompletedAt)
            .LastOrDefault();
        var unit = catalog.RaidBossesView.Primes.FirstOrDefault(candidate => candidate.UnitSetId == encounter.UnitSetId);
        var step = StatStepAt(unit, encounter.ProgressionIndex);
        int? maximumHp = attack?.MaximumHp > 0 ? attack.MaximumHp : step?.Health;
        int? remainingHp = attack?.RemainingHp ?? maximumHp;

        var modifiers = encounter.Modifiers
            .OrderBy(modifier => modifier.HpLost)
            .Select((modifier, index) =>
            {
                if (maximumHp is null or <= 0)
                {
                    return new GuildRaidModifierStatusResponse(
                        modifier.ModifierId, modifier.Type, modifier.Target, modifier.Subtarget,
                        modifier.Amount, null, null);
                }

                var count = encounter.Modifiers.Count;
                var positionIndex = index + 1;
                var scaledHpLost = positionIndex == count
                    ? maximumHp.Value
                    : (int)Math.Round(maximumHp.Value * positionIndex / (double)count, MidpointRounding.AwayFromZero);
                var activationRemainingHp = maximumHp.Value - scaledHpLost;
                var hpLost = maximumHp.Value - (remainingHp ?? maximumHp.Value);
                return new GuildRaidModifierStatusResponse(
                    modifier.ModifierId, modifier.Type, modifier.Target, modifier.Subtarget,
                    modifier.Amount, activationRemainingHp, hpLost >= scaledHpLost);
            })
            .ToArray();

        return new GuildRaidPrimeStatusResponse(
            encounter.EncounterIndex,
            encounter.UnitSetId,
            encounter.ProgressionIndex,
            remainingHp,
            maximumHp,
            modifiers);
    }

    private static int FindPositionIndex(IReadOnlyList<Position> positions, GuildRaidAttack attack)
    {
        for (var index = 0; index < positions.Count; index++)
        {
            var boss = positions[index].Set.Encounters.FirstOrDefault(encounter =>
                string.Equals(encounter.EncounterType, "Boss", StringComparison.OrdinalIgnoreCase));
            if (boss is not null && Matches(attack, positions[index], boss))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"The latest Guild Raid boss '{attack.UnitSetId}' is not in the season config.");
    }

    private static GameCatalogRaidBossStatStepView? StatStepAt(
        GameCatalogRaidBossView? unit,
        int progressionIndex)
    {
        if (unit is null || progressionIndex <= 0 || progressionIndex > unit.StatProgression.Count)
        {
            return null;
        }

        return unit.StatProgression[progressionIndex - 1];
    }

    private static GuildRaidDifficulty ParseDifficulty(string value) =>
        Enum.TryParse<GuildRaidDifficulty>(value, ignoreCase: true, out var difficulty)
            ? difficulty
            : throw new InvalidOperationException($"Unknown Guild Raid difficulty '{value}'.");

    private static bool Matches(GuildRaidAttack attack, Position position, GameCatalogRaidBossEncounterView encounter) =>
        attack.Tier == position.Tier.Tier
        && attack.Set == position.Set.Set
        && attack.EncounterIndex == encounter.EncounterIndex
        && attack.UnitSetId == encounter.UnitSetId;

    private static int NextPositionIndex(
        IReadOnlyList<Position> positions,
        int currentIndex,
        string seasonConfigId,
        GameCatalogSnapshot catalog)
    {
        if (currentIndex + 1 < positions.Count)
        {
            return currentIndex + 1;
        }

        var raw = catalog.RaidBossRawData.Seasons.Values.FirstOrDefault(season =>
            season.GuildBossSeasonConfigId == seasonConfigId);
        if (raw?.LoopFromTier is null || raw.LoopFromSet is null)
        {
            return currentIndex;
        }

        var loopIndex = positions
            .Select((position, index) => (position, index))
            .FirstOrDefault(item => item.position.Tier.Tier == raw.LoopFromTier && item.position.Set.Set == raw.LoopFromSet)
            .index;
        return loopIndex;
    }

    private static DateTimeOffset? ResolveEndsAt(
        GuildRaidSeason season,
        DateTimeOffset observedAt,
        GameCatalogSnapshot catalog)
    {
        return catalog.EventOccurrences
            .Where(occurrence => occurrence.DefinitionId == "guild-raid-season"
                && occurrence.StartUtc <= observedAt
                && occurrence.EndUtc > observedAt
                && MatchesSeasonOccurrence(occurrence, season))
            .Select(occurrence => (DateTimeOffset?)occurrence.EndUtc)
            .FirstOrDefault();
    }

    private static bool MatchesSeasonOccurrence(GameCatalogEventOccurrence occurrence, GuildRaidSeason season)
    {
        if (occurrence.Parameters is null)
        {
            return false;
        }

        var seasonNumberMatches = occurrence.Parameters.TryGetValue("season", out var number)
            && number.ValueKind == System.Text.Json.JsonValueKind.Number
            && number.TryGetInt32(out var parsed)
            && parsed == season.SeasonNumber;
        var configMatches = occurrence.Parameters.TryGetValue("seasonConfigId", out var config)
            && config.ValueKind == System.Text.Json.JsonValueKind.String
            && config.GetString() == season.SeasonConfigId;
        return seasonNumberMatches || configMatches;
    }

    private sealed record Position(
        GameCatalogRaidBossTierView Tier,
        GameCatalogRaidBossSetView Set,
        int TierIndex,
        int SetIndex);
}
