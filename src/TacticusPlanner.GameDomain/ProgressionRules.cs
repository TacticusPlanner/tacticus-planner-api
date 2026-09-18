namespace TacticusPlanner.GameDomain;

/// <summary>Pure progression/ability ordering rules — no catalog or player data, matches the frontend's
/// <c>packages/game-domain</c>. Catalog-bound rules (that need actual game data to resolve, e.g. Onslaught
/// rewards or per-character upgrade requirements) stay in <c>TacticusPlanner.GameCatalog</c>.</summary>
public static class ProgressionRules
{
    public static readonly string[] ProgressionOrder =
    [
        "Common:None", "Common:OneStar", "Common:TwoStars",
        "Uncommon:TwoStars", "Uncommon:ThreeStars", "Uncommon:FourStars",
        "Rare:FourStars", "Rare:FiveStars", "Rare:RedOneStar",
        "Epic:RedOneStar", "Epic:RedTwoStars", "Epic:RedThreeStars",
        "Legendary:RedThreeStars", "Legendary:RedFourStars", "Legendary:RedFiveStars",
        "Legendary:OneBlueStar", "Mythic:OneBlueStar", "Mythic:TwoBlueStars",
        "Mythic:ThreeBlueStars", "Mythic:MythicWings",
    ];

    public static int ProgressionIndex(string progression) =>
        Array.IndexOf(ProgressionOrder, progression);

    private static readonly Dictionary<string, int> AbilityCaps =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Common"] = 8,
            ["Uncommon"] = 17,
            ["Rare"] = 26,
            ["Epic"] = 35,
            ["Legendary"] = 50,
            ["Mythic"] = 60,
        };

    public static int AbilityCapForRarity(string rarity) =>
        AbilityCaps.TryGetValue(rarity, out var cap)
            ? cap
            : throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Unsupported rarity.");

    public static string RarityFor(UnitProgression progression) => progression switch
    {
        >= UnitProgression.CommonNone and <= UnitProgression.CommonTwoStars => "Common",
        >= UnitProgression.UncommonTwoStars and <= UnitProgression.UncommonFourStars => "Uncommon",
        >= UnitProgression.RareFourStars and <= UnitProgression.RareRedOneStar => "Rare",
        >= UnitProgression.EpicRedOneStar and <= UnitProgression.EpicRedThreeStars => "Epic",
        >= UnitProgression.LegendaryRedThreeStars and <= UnitProgression.LegendaryOneBlueStar => "Legendary",
        >= UnitProgression.MythicOneBlueStar and <= UnitProgression.MythicMythicWings => "Mythic",
        _ => throw new ArgumentOutOfRangeException(nameof(progression), progression, "Unsupported progression."),
    };

    public static int AbilityCapForProgression(UnitProgression progression) =>
        AbilityCapForRarity(RarityFor(progression));

    // The tables below back automatic prerequisite synthesis for V1 goal import
    // (rewrite-v1-goal-import) and mirror the client's `packages/game-domain/src/progression.ts` and
    // `apps/web/.../goal-farming/lib/rank-additional-target.ts` exactly (deliberately duplicated, not
    // shared — see that change's design.md). Keep both sides in sync by hand when the game's ladder
    // changes; cross-checked by TacticusPlanner.Api.Tests' ProgressionRulesPrerequisiteTests against
    // literal copies of the client's tables.

    /// <summary>The highest rank a unit at this progression's rarity can ever reach — mirrors the
    /// client's <c>maxRankByRarity</c>/<c>maxRankForProgression</c>.</summary>
    private static readonly Dictionary<string, UnitRank> MaxRankByRarity = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Common"] = UnitRank.Iron1,
        ["Uncommon"] = UnitRank.Bronze1,
        ["Rare"] = UnitRank.Silver1,
        ["Epic"] = UnitRank.Gold1,
        ["Legendary"] = UnitRank.Diamond3,
        ["Mythic"] = UnitRank.Adamantine2,
    };

    public static UnitRank MaxRankForProgression(UnitProgression progression) => MaxRankByRarity[RarityFor(progression)];

    /// <summary>The lowest progression whose rarity allows at least <paramref name="rank"/> — the
    /// Ascension target an above-cap Rank goal implies. Mirrors the client's
    /// <c>minProgressionForRank</c>.</summary>
    public static UnitProgression MinimumProgressionForRank(UnitRank rank) =>
        FirstOrLast(AllProgressions, progression => MaxRankForProgression(progression) >= rank);

    /// <summary>The lowest progression whose rarity ability cap is at least <paramref name="level"/> —
    /// the inverse of <see cref="AbilityCapForProgression"/>, mirroring the client's
    /// <c>minProgressionForAbilityLevel</c>.</summary>
    public static UnitProgression MinimumProgressionForAbilityLevel(int level) =>
        FirstOrLast(AllProgressions, progression => AbilityCapForProgression(progression) >= level);

    private static readonly UnitProgression[] AllProgressions = Enum.GetValues<UnitProgression>();

    /// <summary>Mirrors the client's <c>progressionOrder.find(...) ?? lastProgression</c> pattern: the
    /// first value satisfying <paramref name="predicate"/>, or the ladder's last value when nothing above
    /// the target exists (e.g. a rank/ability level beyond the Mythic tier's own cap).</summary>
    private static UnitProgression FirstOrLast(UnitProgression[] progressions, Func<UnitProgression, bool> predicate)
    {
        foreach (var progression in progressions)
        {
            if (predicate(progression)) return progression;
        }

        return progressions[^1];
    }

    /// <summary>The character level required to have already reached <paramref name="rank"/>, given
    /// the end-of-target partial-upgrade selection V1/V2 both encode as
    /// (<paramref name="endPointFive"/>, <paramref name="endAppliedUpgrades"/>) — mirrors the client's
    /// <c>rankToLevel</c> table combined with <c>additionalTargetFromWire</c> +
    /// <c>requiredLevelForRankTarget</c>. Below Adamantine1 a rank's upgrades are a 3-slot "top row"
    /// (pointFive or 3+ applied upgrades means all 3); at Adamantine1+ the 6 slots are numbered
    /// individually and each of the first 5 has its own level.</summary>
    public static int RequiredLevelForRankTarget(UnitRank rank, bool endPointFive, int endAppliedUpgrades)
    {
        var baseLevel = RankToLevel[rank];
        if (rank >= UnitRank.Adamantine1)
        {
            return endAppliedUpgrades > 0 ? baseLevel + Math.Min(endAppliedUpgrades, 5) - 1 : baseLevel;
        }

        if (endPointFive || endAppliedUpgrades >= 3) return baseLevel + 2;
        if (endAppliedUpgrades == 2) return baseLevel + 1;
        return baseLevel;
    }

    /// <summary>Ported from V1's <c>rankToLevel</c> (<c>models/constants.ts</c>), mirroring the client's
    /// <c>rank-additional-target.ts</c> copy. No entry above Adamantine2 — Adamantine3 is not a reachable
    /// rank target on the client either; <see cref="RequiredLevelForRankTarget"/> falls back to
    /// Adamantine2's level for it rather than throwing.</summary>
    private static readonly Dictionary<UnitRank, int> RankToLevel = new()
    {
        [UnitRank.Stone1] = 1,
        [UnitRank.Stone2] = 3,
        [UnitRank.Stone3] = 5,
        [UnitRank.Iron1] = 8,
        [UnitRank.Iron2] = 11,
        [UnitRank.Iron3] = 14,
        [UnitRank.Bronze1] = 17,
        [UnitRank.Bronze2] = 20,
        [UnitRank.Bronze3] = 23,
        [UnitRank.Silver1] = 26,
        [UnitRank.Silver2] = 29,
        [UnitRank.Silver3] = 32,
        [UnitRank.Gold1] = 35,
        [UnitRank.Gold2] = 38,
        [UnitRank.Gold3] = 41,
        [UnitRank.Diamond1] = 44,
        [UnitRank.Diamond2] = 47,
        [UnitRank.Diamond3] = 50,
        [UnitRank.Adamantine1] = 55,
        [UnitRank.Adamantine2] = 60,
        [UnitRank.Adamantine3] = 60,
    };
}
