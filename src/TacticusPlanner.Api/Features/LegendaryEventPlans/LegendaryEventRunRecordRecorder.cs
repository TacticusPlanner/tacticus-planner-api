using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.LegendaryEventPlans;

/// <summary>
/// Opens or refreshes Legendary Event run records from a sync's incoming <c>lre-progress</c> (LRE Stage 7, design
/// D2–D4). Runs inside <c>PlayerDataSyncService.SyncAsync</c> after the transform and before the save, so records
/// commit with the snapshot. Only stages changes on the context; the caller saves.
/// </summary>
public sealed class LegendaryEventRunRecordRecorder(PlannerDbContext db, IGameCatalogProvider catalog)
{
    public async Task ApplyAsync(
        ProfileId profileId,
        IReadOnlyList<LreProgressRecord> incoming,
        IReadOnlyList<PlayerCharacterRecord> characters,
        IReadOnlyList<PlayerMowRecord> mows,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var catalogIds = catalog.Current.LreViews.Select(view => view.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var entry in incoming)
        {
            var eventId = entry.Id.Value;
            if (!catalogIds.Contains(eventId)
                || entry.CurrentEventRun is not (>= LegendaryEventValidation.MinRun and <= LegendaryEventValidation.MaxRun)
                || !HasEncounters(entry))
            {
                continue;
            }

            var run = entry.CurrentEventRun.Value;
            var lanes = CompactLanes(entry);
            var latest = await db.LegendaryEventRunRecords
                .Where(record => record.ProfileId == profileId && record.EventId == eventId)
                .OrderByDescending(record => record.StartedAt)
                .ThenByDescending(record => record.Id)
                .FirstOrDefaultAsync(ct);

            if (latest is null || latest.Run != run || !IsSuperset(lanes, latest.Lanes))
            {
                db.LegendaryEventRunRecords.Add(new LegendaryEventRunRecord
                {
                    Id = LegendaryEventRunRecordId.From(Guid.CreateVersion7()),
                    ProfileId = profileId,
                    EventId = eventId,
                    Run = run,
                    StartedAt = now,
                    ProgressAt = now,
                    CatalogVersion = GameCatalogRelease.Version,
                    Points = entry.CurrentPoints,
                    Lanes = lanes,
                    Teams = await LoadTeamsAsync(profileId, eventId, ct),
                    Roster = NewRoster(characters, mows, now),
                });
                continue;
            }

            if (LanesEqual(lanes, latest.Lanes))
                continue;

            latest.ProgressAt = now;
            latest.CatalogVersion = GameCatalogRelease.Version;
            latest.Points = entry.CurrentPoints;
            latest.Lanes = lanes;
            latest.Teams = await LoadTeamsAsync(profileId, eventId, ct);
            latest.Roster = NewRoster(characters, mows, now);
        }
    }

    /// <summary>Drops whatever <see cref="ApplyAsync"/> staged, so a retried save can apply it again against
    /// fresh rows without duplicating records.</summary>
    public void Discard()
    {
        var root = typeof(LegendaryEventRunRecord).FullName!;
        var staged = db.ChangeTracker.Entries()
            .Where(entry => entry.Metadata.Name == root
                || entry.Metadata.Name.StartsWith(root + ".", StringComparison.Ordinal))
            .ToList();
        foreach (var entry in staged)
            entry.State = EntityState.Detached;
    }

    /// <summary>Copies of the plan's teams, in lane then sort order (the served depth is picked per record run).</summary>
    private async Task<List<LegendaryEventTeamRecord>> LoadTeamsAsync(ProfileId profileId, string eventId, CancellationToken ct)
    {
        var plan = await db.LegendaryEventPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.ProfileId == profileId && entity.EventId == eventId, ct);
        if (plan is null)
            return [];

        return plan.Teams
            .OrderBy(team => LegendaryEventPlanProjection.LaneOrder(team.LaneId))
            .ThenBy(team => team.SortOrder)
            .Select(team => team.Clone())
            .ToList();
    }

    private static RosterSnapshot NewRoster(
        IReadOnlyList<PlayerCharacterRecord> characters, IReadOnlyList<PlayerMowRecord> mows, DateTimeOffset now) =>
        new()
        {
            TakenAt = now,
            CatalogVersion = GameCatalogRelease.Version,
            Characters = characters.OrderBy(unit => unit.UnitId.Value, StringComparer.Ordinal).Select(CloneCharacter).ToList(),
            Mows = mows.OrderBy(unit => unit.UnitId.Value, StringComparer.Ordinal).Select(CloneMow).ToList(),
        };

    private static bool HasEncounters(LreProgressRecord entry) =>
        entry.Alpha?.Encounters.Count > 0 || entry.Beta?.Encounters.Count > 0 || entry.Gamma?.Encounters.Count > 0;

    internal static LegendaryEventRunRecordLanes CompactLanes(LreProgressRecord entry) =>
        new() { Alpha = CompactLane(entry.Alpha), Beta = CompactLane(entry.Beta), Gamma = CompactLane(entry.Gamma) };

    private static LegendaryEventRunRecordLane? CompactLane(LreTrackProgressRecord? track) =>
        track is null
            ? null
            : new LegendaryEventRunRecordLane
            {
                Encounters = track.Encounters
                    .Select(encounter => new LegendaryEventRunRecordEncounter
                    {
                        ObjectivesCleared = [.. encounter.ObjectivesCleared],
                        HighScore = encounter.HighScore,
                    })
                    .ToList(),
            };

    /// <summary>Whether <paramref name="incoming"/> keeps every stored battle and every stored cleared objective;
    /// when it does not, the run was reset (design D2).</summary>
    internal static bool IsSuperset(LegendaryEventRunRecordLanes incoming, LegendaryEventRunRecordLanes stored) =>
        LaneIsSuperset(incoming.Alpha, stored.Alpha)
        && LaneIsSuperset(incoming.Beta, stored.Beta)
        && LaneIsSuperset(incoming.Gamma, stored.Gamma);

    private static bool LaneIsSuperset(LegendaryEventRunRecordLane? incoming, LegendaryEventRunRecordLane? stored)
    {
        if (stored is null || stored.Encounters.Count == 0)
            return true;
        if (incoming is null || incoming.Encounters.Count < stored.Encounters.Count)
            return false;

        for (var battle = 0; battle < stored.Encounters.Count; battle++)
        {
            var cleared = incoming.Encounters[battle].ObjectivesCleared.ToHashSet();
            if (!stored.Encounters[battle].ObjectivesCleared.All(cleared.Contains))
                return false;
        }

        return true;
    }

    internal static bool LanesEqual(LegendaryEventRunRecordLanes left, LegendaryEventRunRecordLanes right) =>
        LaneEqual(left.Alpha, right.Alpha) && LaneEqual(left.Beta, right.Beta) && LaneEqual(left.Gamma, right.Gamma);

    private static bool LaneEqual(LegendaryEventRunRecordLane? left, LegendaryEventRunRecordLane? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        if (left.Encounters.Count != right.Encounters.Count)
            return false;

        for (var battle = 0; battle < left.Encounters.Count; battle++)
        {
            var a = left.Encounters[battle];
            var b = right.Encounters[battle];
            if (a.HighScore != b.HighScore || !a.ObjectivesCleared.Order().SequenceEqual(b.ObjectivesCleared.Order()))
                return false;
        }

        return true;
    }

    // The synced chunk instances may also be owned by the player-data snapshot in this same unit of work, and EF
    // tracks an owned instance under one owner only, so the record's roster gets its own copies.
    private static PlayerCharacterRecord CloneCharacter(PlayerCharacterRecord unit) =>
        new()
        {
            UnitId = unit.UnitId,
            ProgressionIndex = unit.ProgressionIndex,
            Xp = unit.Xp,
            XpLevel = unit.XpLevel,
            Shards = unit.Shards,
            MythicShards = unit.MythicShards,
            Abilities = unit.Abilities.Select(CloneAbility).ToList(),
            AppliedUpgradeSlots = [.. unit.AppliedUpgradeSlots],
            Rank = unit.Rank,
            EquippedItems = unit.EquippedItems
                .Select(item => new PlayerUnitEquipmentSlotRecord
                {
                    SlotId = item.SlotId,
                    EquipmentId = item.EquipmentId,
                    Level = item.Level,
                })
                .ToList(),
        };

    private static PlayerMowRecord CloneMow(PlayerMowRecord unit) =>
        new()
        {
            UnitId = unit.UnitId,
            ProgressionIndex = unit.ProgressionIndex,
            Xp = unit.Xp,
            XpLevel = unit.XpLevel,
            Shards = unit.Shards,
            MythicShards = unit.MythicShards,
            Abilities = unit.Abilities.Select(CloneAbility).ToList(),
            AppliedUpgradeSlots = [.. unit.AppliedUpgradeSlots],
        };

    private static PlayerUnitAbilityRecord CloneAbility(PlayerUnitAbilityRecord ability) =>
        new() { AbilityId = ability.AbilityId, Level = ability.Level };
}
