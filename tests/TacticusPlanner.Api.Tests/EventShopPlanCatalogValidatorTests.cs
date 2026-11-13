using TacticusPlanner.Api.Features.EventShopPlans;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;

namespace TacticusPlanner.Api.Tests;

/// <summary>The 400 rules of PUT /event-shop-plans/{eventId}, over the pinned catalog's October 2026 event (one week,
/// slot 1 offers 25 Yarrick shards for 45 on MON/THU/SUN, cap 5).</summary>
public sealed class EventShopPlanCatalogValidatorTests
{
    private static readonly IReadOnlyList<GameCatalogEventShopView> Events = GameCatalogLoader.Load().EventShopViews;

    private static readonly GameCatalogEventShopView October = Events.Single(view => view.Id == "october-2026");

    // August 2026 sells draft (pick-an-alliance) orbs and badges; October sells none.
    private static readonly GameCatalogEventShopView August = Events.Single(view => view.Id == "august-2026");

    private static EventShopCartEntryRequest Yarrick(
        int quantity = 2, int purchased = 1, string day = "THU", int week = 1, int slot = 1, string? alliance = null,
        string reward = "shards_astraYarrick") =>
        new(week, day, slot, reward, quantity, purchased, alliance);

    private static EventShopFieldFailure Single(params EventShopCartEntryRequest[] entries) =>
        Assert.Single(EventShopPlanCatalogValidator.ValidateEntries(October, entries));

    private static (int Slot, string Day, string Reward) DraftOffer()
    {
        for (var slot = 0; slot < August.Weeks[0].Slots.Count; slot++)
        {
            var variant = August.Weeks[0].Slots[slot].Variants
                .FirstOrDefault(candidate => candidate.Reward.Type.StartsWith("draft_", StringComparison.Ordinal));
            if (variant is not null)
                return (slot, variant.Days[0], variant.Reward.Type);
        }

        throw new InvalidOperationException("August 2026 week 1 has no draft offer.");
    }

    [Fact]
    public void AValidCartPasses() =>
        Assert.Empty(EventShopPlanCatalogValidator.ValidateEntries(October, [Yarrick(), Yarrick(day: "SUN", quantity: 5, purchased: 5)]));

    [Fact]
    public void MissingEntriesIsRejected() =>
        Assert.Equal("entries", Assert.Single(EventShopPlanCatalogValidator.ValidateEntries(October, null)).Field);

    [Fact]
    public void MoreThanAThousandEntriesIsRejected() =>
        Assert.Equal("entries", Single([.. Enumerable.Repeat(Yarrick(), 1001)]).Field);

    [Fact]
    public void QuantityAboveTheCapIsRejected() =>
        Assert.Equal("entries[0].quantity", Single(Yarrick(quantity: 6, purchased: 0)).Field);

    [Fact]
    public void ZeroQuantityIsRejected() =>
        Assert.Equal("entries[0].quantity", Single(Yarrick(quantity: 0, purchased: 0)).Field);

    [Fact]
    public void RewardNotOfferedThatDayIsRejected() =>
        Assert.Equal("entries[0].rewardType", Single(Yarrick(day: "TUE")).Field);

    [Fact]
    public void PurchasedAboveQuantityIsRejected() =>
        Assert.Equal("entries[0].purchasedQuantity", Single(Yarrick(quantity: 2, purchased: 3)).Field);

    [Fact]
    public void WeekOutsideTheEventIsRejected() =>
        Assert.Equal("entries[0].week", Single(Yarrick(week: 2)).Field);

    [Fact]
    public void UnknownDayIsRejected() =>
        Assert.Equal("entries[0].day", Single(Yarrick(day: "MONDAY")).Field);

    [Fact]
    public void SlotOutsideTheWeekIsRejected() =>
        Assert.Equal("entries[0].slotIndex", Single(Yarrick(slot: October.Weeks[0].Slots.Count)).Field);

    [Fact]
    public void DuplicateSlotDayIsRejected() =>
        Assert.Equal("entries[1]", Single(Yarrick(), Yarrick(quantity: 1, purchased: 0)).Field);

    [Fact]
    public void DraftAllianceOnANonDraftRewardIsRejected() =>
        Assert.Equal("entries[0].draftAlliance", Single(Yarrick(alliance: "xenos")).Field);

    [Fact]
    public void DraftRewardTakesAKnownAlliance()
    {
        var (slot, day, reward) = DraftOffer();
        EventShopCartEntryRequest Draft(string alliance) =>
            Yarrick(slot: slot, day: day, reward: reward, quantity: 1, purchased: 0, alliance: alliance);

        Assert.Empty(EventShopPlanCatalogValidator.ValidateEntries(August, [Draft("xenos")]));
        Assert.Equal("entries[0].draftAlliance",
            Assert.Single(EventShopPlanCatalogValidator.ValidateEntries(August, [Draft("orks")])).Field);
    }

    [Fact]
    public void DayOrderStartsOnTheEventsWeekday()
    {
        Assert.Equal(["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"], EventShopPlanCatalogValidator.DayOrder(October));
        var wednesdayStart = October with { StartUtc = new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero) };
        Assert.Equal(["WED", "THU", "FRI", "SAT", "SUN", "MON", "TUE"], EventShopPlanCatalogValidator.DayOrder(wednesdayStart));
    }
}
