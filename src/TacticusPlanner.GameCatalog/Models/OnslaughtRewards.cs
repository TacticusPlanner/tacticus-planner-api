namespace TacticusPlanner.GameCatalog.Models;

public sealed record GameCatalogRewardRange(int Min, int Max)
{
    public double Midpoint => (Min + Max) / 2d;
}

public sealed record GameCatalogOnslaughtReward(
    string Id,
    string Sector,
    int Tier,
    IReadOnlyList<GameCatalogRewardRange> Regular,
    GameCatalogRewardRange Mythic,
    IReadOnlyList<GameCatalogAmountByRarity> Badges);
