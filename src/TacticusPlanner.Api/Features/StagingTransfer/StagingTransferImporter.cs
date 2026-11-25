using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.EventShops;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.GuildWars;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Domain.RosterSnapshots;
using TacticusPlanner.Domain.Teams;
using TacticusPlanner.Persistence;
using UserSettingsEntity = TacticusPlanner.Domain.UserSettings.UserSettings;

namespace TacticusPlanner.Api.Features.StagingTransfer;

public sealed record StagingTransferImportCounts(
    int Goals,
    int Projects,
    int LegendaryEventPlans,
    int LegendaryEventRunRecords,
    int PersonalRosterSnapshots,
    int EventShopPlans,
    int Teams,
    int GuildWars,
    bool Settings,
    bool Overrides);

/// <summary>
/// Staging side: replaces the caller's planner data with a production bundle, in one transaction. Goals,
/// projects, Legendary Event plans and run records, event shop plans, personal roster snapshots, teams, Guild Wars, user settings and player-data overrides are deleted and
/// recreated with fresh ids (so two staging accounts can import the same production profile). The display
/// name is copied only when the staging account has not confirmed one. The Tacticus integration, player-data
/// snapshot and guild membership are left as they are.
/// </summary>
public sealed class StagingTransferImporter(PlannerDbContext db, TimeProvider timeProvider)
{
    public async Task<StagingTransferImportCounts> ImportAsync(
        ProfileId profileId,
        StagingTransferBundle bundle,
        CancellationToken ct)
    {
        var goalIds = bundle.Goals
            .Select(goal => goal.Id)
            .Distinct()
            .ToDictionary(id => id, _ => GoalId.From(Guid.CreateVersion7()));

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Serializes with other profile-level writers, and with a second redemption of the same code.
            var profile = await db.LockCurrentProfileAsync(ct);

            // Project-goal links cascade with either side.
            await db.Goals.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.Projects.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.LegendaryEventPlans.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.LegendaryEventRunRecords.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.PersonalRosterSnapshots.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.EventShopPlans.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.Teams.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            // Attacks cascade with their war.
            await db.GuildWars.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.UserSettings.Where(entity => entity.Id == profileId).ExecuteDeleteAsync(ct);
            await db.PlayerDataOverrides.Where(entity => entity.Id == profileId).ExecuteDeleteAsync(ct);

            profile.GoalOrderRevision++;
            if (string.IsNullOrEmpty(profile.DisplayName) && !string.IsNullOrWhiteSpace(bundle.DisplayName))
            {
                profile.DisplayName = bundle.DisplayName.Trim();
            }

            if (bundle.Settings is { } settings)
            {
                db.UserSettings.Add(new UserSettingsEntity { Id = profileId, Settings = settings });
            }

            if (bundle.Overrides is { } overrides)
            {
                db.PlayerDataOverrides.Add(new PlayerDataOverride
                {
                    Id = profileId,
                    CampaignEventProgressOverrides = overrides.CampaignEventProgress,
                    OnslaughtProgressOverrides = overrides.OnslaughtProgress,
                    BattleProgressOverrides = overrides.BattleProgress,
                    IncursionBattleProgressOverrides = overrides.IncursionBattleProgress,
                });
            }

            foreach (var source in bundle.Goals.DistinctBy(goal => goal.Id))
            {
                db.Goals.Add(new Goal(source.EntityType, source.EntityId, source.GoalType)
                {
                    Id = goalIds[source.Id],
                    ProfileId = profileId,
                    Status = source.Status,
                    GlobalPriority = source.GlobalPriority,
                    Notes = source.Notes,
                    Config = source.Config,
                    Snapshot = source.Snapshot,
                    // A dependency on a goal that is not in the bundle has nothing to point at here.
                    DependsOn = source.DependsOn
                        .Where(goalIds.ContainsKey)
                        .Select(id => goalIds[id].Value)
                        .ToList(),
                    LinkedGoalIds = source.LinkedGoalIds
                        .Where(goalIds.ContainsKey)
                        .Select(id => goalIds[id].Value)
                        .ToList(),
                });
            }

            var now = timeProvider.GetUtcNow();
            foreach (var source in bundle.Projects.DistinctBy(project => project.Id))
            {
                var project = new Project
                {
                    Id = ProjectId.From(Guid.CreateVersion7()),
                    ProfileId = profileId,
                    Name = source.Name,
                    Description = source.Description,
                    Color = source.Color,
                    Status = source.Status,
                    Type = source.Type,
                    TargetKind = source.TargetKind,
                    TargetEventId = source.TargetEventId,
                };
                var linkedAt = now;
                foreach (var goalId in source.GoalIds.Distinct().Where(goalIds.ContainsKey))
                {
                    // Membership order is creation order; keep production's by spacing the timestamps.
                    project.ProjectGoals.Add(new ProjectGoal
                    {
                        ProjectId = project.Id,
                        GoalId = goalIds[goalId],
                        CreatedAt = linkedAt,
                    });
                    linkedAt = linkedAt.AddTicks(10);
                }

                db.Projects.Add(project);
            }

            foreach (var source in bundle.LegendaryEventPlans.DistinctBy(plan => plan.EventId))
            {
                db.LegendaryEventPlans.Add(new LegendaryEventPlan
                {
                    Id = LegendaryEventPlanId.From(Guid.CreateVersion7()),
                    ProfileId = profileId,
                    EventId = source.EventId,
                    CatalogVersion = source.CatalogVersion,
                    Notes = source.Notes,
                    ShowPaidOptions = source.ShowPaidOptions,
                    Teams = source.Teams,
                    Runs = source.Runs,
                    Annotations = source.Annotations,
                });
            }

            foreach (var source in bundle.LegendaryEventRunRecords)
            {
                db.LegendaryEventRunRecords.Add(new LegendaryEventRunRecord
                {
                    Id = LegendaryEventRunRecordId.From(Guid.CreateVersion7()),
                    ProfileId = profileId,
                    EventId = source.EventId,
                    Run = source.Run,
                    StartedAt = source.StartedAt,
                    ProgressAt = source.ProgressAt,
                    CatalogVersion = source.CatalogVersion,
                    Points = source.Points,
                    Lanes = source.Lanes,
                    Teams = source.Teams,
                    Roster = source.Roster,
                });
            }

            var importKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (source, index) in bundle.PersonalRosterSnapshots.Select((snapshot, index) => (snapshot, index)))
            {
                // The roster carries its own takenAt; the exported one wins so the metadata matches it.
                source.Roster.TakenAt = source.TakenAt;
                db.PersonalRosterSnapshots.Add(PersonalRosterSnapshot.Create(
                    profileId, source.Name, source.Source, source.Roster, ImportKeyFor(source, index, importKeys)));
            }

            foreach (var source in bundle.EventShopPlans.DistinctBy(plan => plan.EventId))
            {
                var plan = new EventShopPlan
                {
                    Id = EventShopPlanId.From(Guid.CreateVersion7()),
                    ProfileId = profileId,
                    EventId = source.EventId,
                    CatalogVersion = source.CatalogVersion,
                    Entries = source.Entries,
                };

                // The bundle comes from another build; a plan that breaks the stored shape fails the whole import
                // (the transaction is never committed) instead of landing in jsonb.
                EventShopValidation.AssertInvariants(plan);
                db.EventShopPlans.Add(plan);
            }

            // Positions follow the bundle's order. Names are unique in production; an import key repeated by a
            // malformed bundle is dropped rather than failing the whole import on the unique index.
            var teamImportKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (source, index) in bundle.Teams.Select((team, index) => (team, index)))
            {
                var importKey = source.ImportKey is { Length: > 0 and <= TeamValidation.MaxImportKeyLength } key && teamImportKeys.Add(key)
                    ? key
                    : null;
                db.Teams.Add(Team.Create(
                    profileId,
                    new TeamDraft(source.Name, source.Modes, source.CharacterIds, source.FlexStartIndex, source.MowIds,
                        source.CampaignGroupId, source.IncursionMowIds, source.Notes, source.MowFlexStartIndex,
                        source.RarityCap),
                    index + 1,
                    importKey));
            }

            AddGuildWars(profileId, bundle.GuildWars);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return new StagingTransferImportCounts(
            goalIds.Count,
            bundle.Projects.DistinctBy(project => project.Id).Count(),
            bundle.LegendaryEventPlans.DistinctBy(plan => plan.EventId).Count(),
            bundle.LegendaryEventRunRecords.Count,
            bundle.PersonalRosterSnapshots.Count,
            bundle.EventShopPlans.DistinctBy(plan => plan.EventId).Count(),
            bundle.Teams.Count,
            bundle.GuildWars.Count,
            bundle.Settings is not null,
            bundle.Overrides is not null);
    }

    /// <summary>
    /// Recreates the bundle's wars and attacks with new ids, keeping every field. Production holds at most one current
    /// war; should a malformed bundle hold more, only the last started stays current and the others end when it
    /// started, so the one-current-war index cannot fail the whole import.
    /// </summary>
    private void AddGuildWars(ProfileId profileId, IReadOnlyList<StagingTransferGuildWar> wars)
    {
        var ordered = wars.OrderBy(war => war.StartedAt).ToList();
        var current = ordered.LastOrDefault(war => war.EndedAt is null);
        foreach (var source in ordered)
        {
            var war = GuildWar.Start(profileId, source.SeasonId, source.BattlefieldLevel, source.StartedAt);
            war.EndedAt = source.EndedAt ?? (ReferenceEquals(source, current) ? null : current!.StartedAt);
            db.GuildWars.Add(war);
            foreach (var attack in source.Attacks)
            {
                db.GuildWarAttacks.Add(new GuildWarAttack
                {
                    Id = GuildWarAttackId.From(Guid.CreateVersion7()),
                    WarId = war.Id,
                    ProfileId = profileId,
                    AttackedAt = attack.AttackedAt,
                    ZoneId = attack.ZoneId,
                    Slot = attack.Slot,
                    RarityCap = attack.RarityCap,
                    Lineup = attack.Lineup,
                    Result = attack.Result,
                    LostUnitIds = attack.LostUnitIds,
                    Enemy = attack.Enemy,
                    Notes = attack.Notes,
                });
            }
        }
    }

    /// <summary>
    /// The import key a recreated snapshot needs: a V1-imported snapshot must carry one and every other must not
    /// (<see cref="PersonalRosterSnapshot.EnsureValid"/>). An older production omits it from the bundle, and the key
    /// must be unique per profile, so a missing or repeated key is replaced by one derived from the snapshot's place.
    /// </summary>
    public static string? ImportKeyFor(StagingTransferPersonalRosterSnapshot source, int index, ISet<string> used)
    {
        if (source.Source != RosterSnapshotSource.V1Import)
        {
            return null;
        }

        var key = source.ImportKey;
        if (string.IsNullOrEmpty(key) || key.Length > PersonalRosterSnapshotValidation.MaxImportKeyLength || !used.Add(key))
        {
            key = $"staging-transfer:{index}";
            used.Add(key);
        }

        return key;
    }
}
