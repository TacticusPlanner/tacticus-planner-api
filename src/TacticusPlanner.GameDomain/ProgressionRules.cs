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
}
