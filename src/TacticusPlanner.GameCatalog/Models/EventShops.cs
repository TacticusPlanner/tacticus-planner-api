using System.Text.Json;
using System.Text.Json.Serialization;

namespace TacticusPlanner.GameCatalog.Models;

// ---- raw datamine shapes (internal to denormalization) -------------------------------------------

/// <summary>A seasonal quest group of <c>quests/groups.json</c> (<c>seasonal_daily</c>, <c>seasonal_seasonal</c>, ...).</summary>
public sealed record DatamineSeasonalQuestGroup(IReadOnlyList<DatamineSeasonalQuest> Quests);

/// <summary>One seasonal mission; icons, localisation keys and sort priorities are not bound.</summary>
public sealed record DatamineSeasonalQuest(
    string Name,
    IReadOnlyList<string>? Rewards,
    IReadOnlyList<string>? SuccessorQuests,
    IReadOnlyList<DatamineSeasonalQuestTask>? Tasks);

public sealed record DatamineSeasonalQuestTask(
    string Name,
    int Target,
    IReadOnlyDictionary<string, JsonElement>? TaskParameters);

/// <summary>
/// One seasonal event shop as discovered from the datamine and dated by the calendar: its event key, the
/// <c>ShopEvent</c> definition of its weeks, and per week the merchant and calendar occurrence. An event the calendar
/// does not date yet has no definition and no occurrences (<see cref="IsDated"/> is false). Kept on the snapshot for
/// validation and plan-write lookups.
/// </summary>
public sealed record GameCatalogRawEventShop(
    string EventKey,
    string? DefinitionId,
    bool PerWeekMerchants,
    IReadOnlyList<GameCatalogRawEventShopWeek> Weeks)
{
    /// <summary>True when the calendar dates the event (every week has an occurrence).</summary>
    public bool IsDated => Weeks.Count > 0 && Weeks[0].Occurrence is not null;
}

/// <summary>One week of an event shop. <see cref="MerchantKey"/>/<see cref="Merchant"/> are null when the event has
/// per-week merchants but none for this week (validation reports it). <see cref="Occurrence"/> is the calendar week,
/// null while the calendar does not date the event.</summary>
public sealed record GameCatalogRawEventShopWeek(
    int Week,
    string? MerchantKey,
    GameCatalogRawShop? Merchant,
    GameCatalogEventOccurrence? Occurrence);

// ---- served view (public catalog surface) --------------------------------------------------------

/// <summary>
/// One seasonal event shop (<c>event-shops</c> dataset). Ids and structured values only: the client names the event
/// from <see cref="DefinitionId"/> (or, while it is null, from <see cref="EventKey"/>) and derives dates, the current
/// day and every label. <see cref="StartUtc"/> and <see cref="DefinitionId"/> are null for an event the datamine ships
/// but the calendar does not date yet (start date to be announced); its <see cref="DayCount"/> is then 7 per known
/// week. A dated event's <see cref="DayCount"/> is its weeks' total days (the last week may be shorter than 7). See
/// specs/game-event-shops-dataset.
/// </summary>
public sealed record GameCatalogEventShopView(
    string Id,
    string EventKey,
    string? DefinitionId,
    string Currency,
    DateTimeOffset? StartUtc,
    int DayCount,
    IReadOnlyList<GameCatalogRewardTierView> Milestones,
    IReadOnlyList<GameCatalogEventShopWeekView> Weeks);

/// <summary>One week of an event shop: its slots (daily-shop variant projection), ladder and missions.</summary>
public sealed record GameCatalogEventShopWeekView(
    int Week,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    GameCatalogEventShopIncomeView? EstimatedWeeklyIncome,
    IReadOnlyList<GameCatalogShopSlotView> Slots,
    IReadOnlyList<GameCatalogRewardTierView> ProgressChests,
    GameCatalogEventShopMissionsView Missions);

/// <summary>Estimated event currency earned in a week, from the calendar occurrence's income parameters.</summary>
public sealed record GameCatalogEventShopIncomeView(int Free, int BonusShipment);

/// <summary>A progress ladder tier: the progress it needs and the rewards it grants.</summary>
public sealed record GameCatalogRewardTierView(int RequiredProgress, IReadOnlyList<GameCatalogRewardRangeView> Rewards);

/// <summary>A reward as a type and an inclusive quantity range (<c>min == max</c> for a fixed amount).</summary>
public sealed record GameCatalogRewardRangeView(string Type, int Min, int Max);

public sealed record GameCatalogEventShopMissionsView(
    IReadOnlyList<GameCatalogEventShopMissionView> Daily,
    IReadOnlyList<GameCatalogEventShopMissionChainView> Free,
    IReadOnlyList<GameCatalogEventShopMissionChainView> Premium);

public sealed record GameCatalogEventShopMissionChainView(string Id, IReadOnlyList<GameCatalogEventShopMissionView> Missions);

public sealed record GameCatalogEventShopMissionView(
    string Id,
    IReadOnlyList<GameCatalogRewardRangeView> Rewards,
    IReadOnlyList<GameCatalogEventShopTaskView> Tasks);

/// <summary>A mission task: the game's task id, its target and its parameters as shipped (strings or string lists).</summary>
public sealed record GameCatalogEventShopTaskView(
    string TaskId,
    int Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, JsonElement>? Parameters);
