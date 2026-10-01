using Xunit;

namespace TacticusPlanner.GameCatalog.Tests;

public sealed class UnlockEligibilityTests
{
    // Kharn has no campaign shard nodes; the Guild War shop sells his regular shards.
    private const string ShopOnlyCharacter = "worldKharn";

    [Fact]
    public void ShopOnlyCharacterIsEligibleViaItsShopOffer()
    {
        var snapshot = GameCatalogLoader.Load();

        Assert.Empty(snapshot.CharacterViews.Single(c => c.Id == ShopOnlyCharacter).ShardLocations);
        Assert.True(snapshot.IsUnlockEligible(ShopOnlyCharacter));
    }

    [Fact]
    public void ShopOnlyCharacterIsIneligibleWithoutShops()
    {
        var snapshot = GameCatalogLoader.Load() with { ShopViews = [] };

        Assert.False(snapshot.IsUnlockEligible(ShopOnlyCharacter));
    }

    [Fact]
    public void MythicOnlyShopOfferDoesNotMakeACharacterEligible()
    {
        var snapshot = GameCatalogLoader.Load();
        var mythicOnly = snapshot with
        {
            ShopViews = [.. snapshot.ShopViews.Select(shop => shop with
            {
                Slots = [.. shop.Slots.Select(slot => slot with
                {
                    Variants = [.. slot.Variants.Where(v => v.Reward.Type != $"shards_{ShopOnlyCharacter}")]
                })]
            })]
        };

        Assert.False(mythicOnly.IsUnlockEligible(ShopOnlyCharacter));
    }

    [Fact]
    public void CampaignCharacterStaysEligible()
    {
        var snapshot = GameCatalogLoader.Load() with { ShopViews = [] };
        var campaignCharacter = snapshot.CharacterViews.First(c => c.ShardLocations.Count > 0);

        Assert.True(snapshot.IsUnlockEligible(campaignCharacter.Id));
    }
}
