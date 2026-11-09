using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.StagingTransfer;

/// <summary>Production side: reads one profile's planner data into a <see cref="StagingTransferBundle"/>.</summary>
/// <remarks>The export request is anonymous (authenticated by the service key), so no current profile feeds
/// the global query filters: every read ignores them and scopes to <paramref name="profileId"/> explicitly.</remarks>
public sealed class StagingTransferExporter(PlannerDbContext db, TimeProvider timeProvider)
{
    public async Task<StagingTransferBundle?> ExportAsync(ProfileId profileId, CancellationToken ct)
    {
        var displayName = await db.Profiles.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.DisplayName)
            .FirstOrDefaultAsync(ct);
        if (displayName is null)
        {
            return null;
        }

        var settings = await db.UserSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => entity.Settings)
            .FirstOrDefaultAsync(ct);
        var overrides = await db.PlayerDataOverrides.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        var goals = await db.Goals.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .OrderBy(entity => entity.CreatedAt).ThenBy(entity => entity.Id)
            .ToListAsync(ct);
        var projects = await db.Projects.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .Include(entity => entity.ProjectGoals)
            .OrderBy(entity => entity.CreatedAt).ThenBy(entity => entity.Id)
            .ToListAsync(ct);
        var plans = await db.LegendaryEventPlans.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .OrderBy(entity => entity.EventId)
            .ToListAsync(ct);
        var runRecords = await db.LegendaryEventRunRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .OrderBy(entity => entity.EventId).ThenBy(entity => entity.StartedAt).ThenBy(entity => entity.Id)
            .ToListAsync(ct);
        var rosterSnapshots = await db.PersonalRosterSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .OrderBy(entity => entity.TakenAt).ThenBy(entity => entity.Id)
            .ToListAsync(ct);

        return new StagingTransferBundle
        {
            ExportedAt = timeProvider.GetUtcNow(),
            DisplayName = displayName,
            Settings = settings,
            Overrides = overrides is null
                ? null
                : new StagingTransferOverrides
                {
                    CampaignEventProgress = overrides.CampaignEventProgressOverrides,
                    OnslaughtProgress = overrides.OnslaughtProgressOverrides,
                    BattleProgress = overrides.BattleProgressOverrides,
                    IncursionBattleProgress = overrides.IncursionBattleProgressOverrides,
                },
            Goals = goals.Select(goal => new StagingTransferGoal
            {
                Id = goal.Id.Value,
                EntityType = goal.EntityType,
                EntityId = goal.EntityId,
                GoalType = goal.GoalType,
                Status = goal.Status,
                GlobalPriority = goal.GlobalPriority,
                Notes = goal.Notes,
                Config = goal.Config,
                Snapshot = goal.Snapshot,
                DependsOn = goal.DependsOn,
                LinkedGoalIds = goal.LinkedGoalIds,
                CreatedAt = goal.CreatedAt,
            }).ToList(),
            Projects = projects.Select(project => new StagingTransferProject
            {
                Id = project.Id.Value,
                Name = project.Name,
                Description = project.Description,
                Color = project.Color,
                Status = project.Status,
                Type = project.Type,
                TargetKind = project.TargetKind,
                TargetEventId = project.TargetEventId,
                GoalIds = project.ProjectGoals
                    .OrderBy(link => link.CreatedAt).ThenBy(link => link.GoalId)
                    .Select(link => link.GoalId.Value)
                    .ToList(),
                CreatedAt = project.CreatedAt,
            }).ToList(),
            LegendaryEventPlans = plans.Select(plan => new StagingTransferLegendaryEventPlan
            {
                EventId = plan.EventId,
                CatalogVersion = plan.CatalogVersion,
                Notes = plan.Notes,
                ShowPaidOptions = plan.ShowPaidOptions,
                Teams = plan.Teams,
                Runs = plan.Runs,
                Annotations = plan.Annotations,
            }).ToList(),
            LegendaryEventRunRecords = runRecords.Select(record => new StagingTransferLegendaryEventRunRecord
            {
                EventId = record.EventId,
                Run = record.Run,
                StartedAt = record.StartedAt,
                ProgressAt = record.ProgressAt,
                CatalogVersion = record.CatalogVersion,
                Points = record.Points,
                Lanes = record.Lanes,
                Teams = record.Teams,
                Roster = record.Roster,
            }).ToList(),
            PersonalRosterSnapshots = rosterSnapshots.Select(snapshot => new StagingTransferPersonalRosterSnapshot
            {
                Name = snapshot.Name,
                Source = snapshot.Source,
                TakenAt = snapshot.TakenAt,
                Roster = snapshot.Roster,
            }).ToList(),
        };
    }
}
