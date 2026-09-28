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
}
