namespace TacticusPlanner.Domain.GuildWars;

/// <summary>Limits of wars and attacks (<c>guild-war-offense</c>).</summary>
public static class GuildWarValidation
{
    public const int MaxIdLength = 64;

    public const int Slots = 5;

    public const int MaxLineupCharacters = 5;

    public const int MaxEnemyCharacters = 5;

    public const int MaxBuffIds = 30;

    public const int MaxUnitsLost = 6;

    public const int MaxBattlefieldLevel = 5;

    public const int MaxXpLevel = 100;

    public const int MaxAbilityLevel = 100;

    public const int MaxAbilityLevels = 2;

    /// <summary>Attacks per war when the war's season is no longer served (design D7).</summary>
    public const int FallbackMaxAttempts = 10;
}

/// <summary>A season's facts the rules read: its zones and their tier per battlefield level, its tiers' slot caps, buffs
/// and boards, and attacks per war.</summary>
public sealed record GuildWarSeasonFacts(
    int MaxAttempts,
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> ZoneTiersByLevel,
    IReadOnlyDictionary<string, IReadOnlyList<GuildWarRarity>> TierCaps,
    IReadOnlySet<string> ZoneIds,
    IReadOnlySet<string> BuffIds,
    IReadOnlySet<string> BoardIds
);

/// <summary>The catalog facts the war rules read, as plain values so the domain keeps no catalog reference.</summary>
public sealed record GuildWarRuleFacts(
    IReadOnlySet<string> CharacterIds,
    IReadOnlySet<string> MowIds,
    IReadOnlyDictionary<string, GuildWarSeasonFacts> Seasons
);

/// <summary>The target and units of an attack being created or updated.</summary>
public sealed record GuildWarAttackDraft(string ZoneId, int Slot, GuildWarLineup Lineup, GuildWarEnemy? Enemy);

/// <summary>
/// The one evaluator for attack writes (design D6, D7): the first rule an attack breaks, in the order of
/// <see cref="GuildWarAttackRuleCode"/>, or null when it may be saved, plus the rarity cap of its target.
/// </summary>
public static class GuildWarRules
{
    /// <summary>Why <paramref name="seasonId"/> and <paramref name="battlefieldLevel"/> cannot be a war's settings
    /// (<c>guildWarUnknownSeason</c>, <c>guildWarUnknownLevel</c>), or null when they can.</summary>
    public static string? SettingsProblem(GuildWarRuleFacts facts, string seasonId, int battlefieldLevel) =>
        !facts.Seasons.TryGetValue(seasonId, out var season) ? UnknownSeason
        : !season.ZoneTiersByLevel.ContainsKey(battlefieldLevel) ? UnknownLevel
        : null;

    public const string UnknownSeason = "guildWarUnknownSeason";

    public const string UnknownLevel = "guildWarUnknownLevel";

    /// <summary>The rarity cap of <paramref name="zoneId"/> slot <paramref name="slot"/> in <paramref name="war"/>'s
    /// season at its battlefield level, or null when the season, level, zone or slot is not known.</summary>
    public static GuildWarRarity? RarityCap(GuildWarRuleFacts facts, GuildWar war, string zoneId, int slot)
    {
        if (slot is < 0 or >= GuildWarValidation.Slots
            || !facts.Seasons.TryGetValue(war.SeasonId, out var season)
            || !season.ZoneTiersByLevel.TryGetValue(war.BattlefieldLevel, out var tiers)
            || !tiers.TryGetValue(zoneId, out var tierId)
            || !season.TierCaps.TryGetValue(tierId, out var caps))
            return null;
        return caps[slot];
    }

    /// <summary>
    /// The first rule <paramref name="draft"/> breaks in <paramref name="war"/>, or null. <paramref name="replacing"/>
    /// is the attack being updated (its own units and token do not count against it), null on create.
    /// </summary>
    public static GuildWarAttackRuleCode? Evaluate(
        GuildWarAttackDraft draft, GuildWar war, GuildWarAttackId? replacing, GuildWarRuleFacts facts)
    {
        facts.Seasons.TryGetValue(war.SeasonId, out var season);
        if (RarityCap(facts, war, draft.ZoneId, draft.Slot) is null)
            return GuildWarAttackRuleCode.UnknownZone;
        if (!UnitsKnown(draft, facts, season!))
            return GuildWarAttackRuleCode.UnknownUnit;

        var others = war.Attacks.Where(attack => replacing is null || attack.Id != replacing.Value).ToList();
        var deployed = others.SelectMany(attack => attack.Lineup.UnitIds()).ToHashSet(StringComparer.Ordinal);
        if (draft.Lineup.UnitIds().Any(deployed.Contains))
            return GuildWarAttackRuleCode.UnitDeployed;
        if (replacing is null && others.Count >= MaxAttempts(facts, war))
            return GuildWarAttackRuleCode.TokensSpent;
        return null;
    }

    public static int MaxAttempts(GuildWarRuleFacts facts, GuildWar war) =>
        facts.Seasons.TryGetValue(war.SeasonId, out var season) ? season.MaxAttempts : GuildWarValidation.FallbackMaxAttempts;

    private static bool UnitsKnown(GuildWarAttackDraft draft, GuildWarRuleFacts facts, GuildWarSeasonFacts season)
    {
        if (!LineupKnown(draft.Lineup.Characters.Select(unit => unit.UnitId), draft.Lineup.Mow?.UnitId, facts))
            return false;
        if (draft.Enemy is not { } enemy)
            return true;
        return LineupKnown(enemy.Characters.Select(unit => unit.UnitId), enemy.Mow?.UnitId, facts)
            && enemy.BuffIds.All(season.BuffIds.Contains)
            && enemy.BuffIds.Distinct(StringComparer.Ordinal).Count() == enemy.BuffIds.Count
            && (enemy.BoardId is null || season.BoardIds.Contains(enemy.BoardId));
    }

    private static bool LineupKnown(IEnumerable<string> characterIds, string? mowId, GuildWarRuleFacts facts)
    {
        var characters = characterIds.ToList();
        return characters.All(facts.CharacterIds.Contains)
            && characters.Distinct(StringComparer.Ordinal).Count() == characters.Count
            && (mowId is null || facts.MowIds.Contains(mowId));
    }
}
