using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.LegendaryEvents;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.Persistence;
using UserSettingsEntity = TacticusPlanner.Domain.UserSettings.UserSettings;

namespace TacticusPlanner.Api.Features.StagingTransfer;

public sealed record StagingTransferImportCounts(
    int Goals,
    int Projects,
    int LegendaryEventPlans,
    int LegendaryEventRunRecords,
    bool Settings,
    bool Overrides);

/// <summary>
/// Staging side: replaces the caller's planner data with a production bundle, in one transaction. Goals,
/// projects, Legendary Event plans and run records, user settings and player-data overrides are deleted and
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

            // Project-goal links cascade with either side.
            await db.Goals.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.Projects.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.LegendaryEventPlans.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.LegendaryEventRunRecords.Where(entity => entity.ProfileId == profileId).ExecuteDeleteAsync(ct);
            await db.UserSettings.Where(entity => entity.Id == profileId).ExecuteDeleteAsync(ct);
            await db.PlayerDataOverrides.Where(entity => entity.Id == profileId).ExecuteDeleteAsync(ct);

            var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId, ct);
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

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return new StagingTransferImportCounts(
            goalIds.Count,
            bundle.Projects.DistinctBy(project => project.Id).Count(),
            bundle.LegendaryEventPlans.DistinctBy(plan => plan.EventId).Count(),
            bundle.LegendaryEventRunRecords.Count,
            bundle.Settings is not null,
            bundle.Overrides is not null);
    }
}
