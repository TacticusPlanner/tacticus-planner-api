using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.GameDomain;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>Every stored plan of the caller's profile (LRE Stage 7, design D5).</summary>
/// <param name="Plans">Ordered by <c>eventId</c> (ordinal), each exactly as its single <c>GET</c> returns it.</param>
public sealed record LegendaryEventPlanListResponse(IReadOnlyList<LegendaryEventPlanResponse> Plans);

/// <summary>One page of the caller's run records for one event.</summary>
/// <param name="Records">Newest <c>startedAt</c> first, then descending id.</param>
/// <param name="NextAfter">The cursor of the next page; null on the last page.</param>
public sealed record LegendaryEventRunRecordsResponse(
    string EventId, IReadOnlyList<LegendaryEventRunRecordResponse> Records, string? NextAfter);

/// <summary>One run occurrence as of the last sync that changed its progress.</summary>
/// <param name="Run">The event run, 1–3.</param>
/// <param name="StartedAt">The first sync that saw this occurrence (UTC).</param>
/// <param name="ProgressAt">The last sync that changed its lanes (UTC).</param>
/// <param name="CatalogVersion">The catalog version at <c>progressAt</c>.</param>
/// <param name="Points">The synced event points at <c>progressAt</c>.</param>
/// <param name="Teams">The plan's teams at <c>progressAt</c>, by lane then <c>sortOrder</c>; empty when there was no
/// plan or no team.</param>
/// <remarks>The roster is read on demand from <c>.../run-records/{recordId}/roster</c>.</remarks>
public sealed record LegendaryEventRunRecordResponse(
    Guid Id,
    int Run,
    DateTimeOffset StartedAt,
    DateTimeOffset ProgressAt,
    string CatalogVersion,
    int Points,
    LegendaryEventRunRecordLanesResponse Lanes,
    IReadOnlyList<LegendaryEventRunRecordTeamResponse> Teams
);

/// <summary>Each lane is null as synced, or its encounters in battle order.</summary>
public sealed record LegendaryEventRunRecordLanesResponse(
    IReadOnlyList<LegendaryEventRunRecordEncounterResponse>? Alpha,
    IReadOnlyList<LegendaryEventRunRecordEncounterResponse>? Beta,
    IReadOnlyList<LegendaryEventRunRecordEncounterResponse>? Gamma
);

/// <param name="ObjectivesCleared">As synced: 0 for defeat-all, 1–5 for the lane objectives.</param>
public sealed record LegendaryEventRunRecordEncounterResponse(IReadOnlyList<int> ObjectivesCleared, int HighScore);

/// <param name="MemberUnitIds">Line-up in position order.</param>
/// <param name="ObjectiveIndexes">Covered lane objective indexes, ascending.</param>
/// <param name="ExpectedBattleClears">The team's depth for the record's run, or null.</param>
/// <param name="ExpectedBattleClearsSource"><c>estimate</c> or <c>manual</c> when a depth is set.</param>
public sealed record LegendaryEventRunRecordTeamResponse(
    Guid TeamId,
    LegendaryEventLaneId LaneId,
    string Name,
    int SortOrder,
    IReadOnlyList<string> MemberUnitIds,
    string? ReserveUnitId,
    IReadOnlyList<int> ObjectiveIndexes,
    int? ExpectedBattleClears,
    LegendaryEventDepthSource? ExpectedBattleClearsSource
);

/// <summary>A whole owned roster. Same fields as the synced <c>characters</c> / <c>mows</c> chunks, but its own
/// contract, so the stored chunk records can change without changing this response.</summary>
/// <param name="TakenAt">When the units were read (UTC).</param>
public sealed record RosterSnapshotResponse(
    DateTimeOffset TakenAt,
    string CatalogVersion,
    IReadOnlyList<RosterCharacterResponse> Characters,
    IReadOnlyList<RosterMowResponse> Mows
)
{
    public static RosterSnapshotResponse From(RosterSnapshot roster) => new(
        roster.TakenAt,
        roster.CatalogVersion,
        roster.Characters.Select(unit => new RosterCharacterResponse(
            unit.UnitId, unit.Rank, unit.ProgressionIndex, unit.Xp, unit.XpLevel, unit.Shards, unit.MythicShards,
            Abilities(unit),
            unit.AppliedUpgradeSlots,
            unit.EquippedItems.Select(slot => new RosterEquipmentSlotResponse(slot.SlotId, slot.EquipmentId, slot.Level)).ToList()))
            .ToList(),
        roster.Mows.Select(unit => new RosterMowResponse(
            unit.UnitId, unit.ProgressionIndex, unit.Xp, unit.XpLevel, unit.Shards, unit.MythicShards,
            Abilities(unit),
            unit.AppliedUpgradeSlots))
            .ToList());

    private static List<RosterAbilityResponse> Abilities(PlayerBaseUnitRecord unit) =>
        unit.Abilities.Select(ability => new RosterAbilityResponse(ability.AbilityId, ability.Level)).ToList();
}

public sealed record RosterCharacterResponse(
    UnitId UnitId,
    UnitRank Rank,
    UnitProgression ProgressionIndex,
    long Xp,
    int XpLevel,
    long Shards,
    long MythicShards,
    IReadOnlyList<RosterAbilityResponse> Abilities,
    IReadOnlyList<int> AppliedUpgradeSlots,
    IReadOnlyList<RosterEquipmentSlotResponse> EquippedItems
);

public sealed record RosterMowResponse(
    UnitId UnitId,
    UnitProgression ProgressionIndex,
    long Xp,
    int XpLevel,
    long Shards,
    long MythicShards,
    IReadOnlyList<RosterAbilityResponse> Abilities,
    IReadOnlyList<int> AppliedUpgradeSlots
);

public sealed record RosterAbilityResponse(string AbilityId, int Level);

public sealed record RosterEquipmentSlotResponse(string SlotId, string EquipmentId, int Level);
