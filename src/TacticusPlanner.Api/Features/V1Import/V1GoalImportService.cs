using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Projects;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.V1Import;

public sealed class V1GoalImportService(
    PlannerDbContext db,
    ProjectsService projects,
    IGameCatalogProvider catalog,
    TimeProvider timeProvider
)
{
    private static readonly string[] Rarities = ["Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic"];
    private static readonly string[] Stars =
    [
        "None", "OneStar", "TwoStars", "ThreeStars", "FourStars", "FiveStars", "RedOneStar",
        "RedTwoStars", "RedThreeStars", "RedFourStars", "RedFiveStars", "OneBlueStar",
        "TwoBlueStars", "ThreeBlueStars", "MythicWings",
    ];
    private static readonly string[] ProgressionOrder =
    [
        "Common:None", "Common:OneStar", "Common:TwoStars", "Uncommon:TwoStars",
        "Uncommon:ThreeStars", "Uncommon:FourStars", "Rare:FourStars", "Rare:FiveStars",
        "Rare:RedOneStar", "Epic:RedOneStar", "Epic:RedTwoStars", "Epic:RedThreeStars",
        "Legendary:RedThreeStars", "Legendary:RedFourStars", "Legendary:RedFiveStars",
        "Legendary:OneBlueStar", "Mythic:OneBlueStar", "Mythic:TwoBlueStars",
        "Mythic:ThreeBlueStars", "Mythic:MythicWings",
    ];
    private static readonly HashSet<string> Progressions = [.. ProgressionOrder];

    public async Task<V1GoalImportResult> ImportAsync(ProfileId profileId, IReadOnlyList<V1Goal> source, CancellationToken ct)
    {
        var issues = new List<V1ImportIssue>();
        var translated = source
            .OrderBy(goal => goal.Priority)
            .Select(goal => Translate(goal, issues))
            .Where(goal => goal is not null)
            .Cast<TranslatedGoal>()
            .ToList();

        var skipped = source.Count - translated.Count;
        var candidates = CollapseProgressionGoals(translated);
        if (candidates.Count == 0)
        {
            return new V1GoalImportResult(0, 0, skipped, issues);
        }

        var defaultProject = await projects.EnsureDefaultProjectAsync(profileId, ct);
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        var now = timeProvider.GetUtcNow();
        var keys = candidates.Select(candidate => candidate.Key).ToHashSet();

        var existing = await db.Goals
            .Where(goal => goal.ProfileId == profileId && goal.Status != GoalStatus.Deleted)
            .ToListAsync(ct);
        var replaced = existing.Where(goal => keys.Contains(new GoalKey(goal.EntityType, goal.EntityId, goal.GoalType))).ToList();
        var replacedIds = replaced.Select(goal => goal.Id).ToList();

        if (replacedIds.Count > 0)
        {
            var memberships = await db.ProjectGoals.Where(link => replacedIds.Contains(link.GoalId)).ToListAsync(ct);
            db.ProjectGoals.RemoveRange(memberships);
            foreach (var goal in replaced)
            {
                goal.Status = GoalStatus.Deleted;
                goal.Events.Add(new GoalEvent { At = now, Type = GoalEventType.Deleted });
            }
        }

        var nextPriority = await projects.GetNextPriorityAsync(defaultProject.Id, ct);
        var imported = candidates.Select((candidate, index) => BuildGoal(profileId, candidate, now, defaultProject.Id, nextPriority + index)).ToList();
        db.Goals.AddRange(imported.Select(item => item.Goal));
        db.ProjectGoals.AddRange(imported.Select(item => item.Link));
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return new V1GoalImportResult(imported.Count, replaced.Count, skipped, issues);
    }

    private TranslatedGoal? Translate(V1Goal source, List<V1ImportIssue> issues)
    {
        if (source.Type is 6 or 7)
        {
            issues.Add(new V1ImportIssue("unsupported_goal_type", source.Id, $"V1 goal type {source.Type} is deferred."));
            return null;
        }

        var snapshot = catalog.Current;
        var isMow = source.Type == 4;
        var entityId = isMow
            ? snapshot.Mows.FirstOrDefault(item => Matches(item.Id, item.Name, source.UnitId ?? source.Character))?.Id
            : snapshot.Characters.FirstOrDefault(item => Matches(item.Id, item.Name, source.Character))?.Id;

        if (entityId is null)
        {
            issues.Add(new V1ImportIssue("unknown_entity", source.Id, "The goal's character or Machine of War is not in the V2 Game Catalog."));
            return null;
        }

        var entityType = isMow ? GoalEntityType.Mow : GoalEntityType.Character;
        GoalType goalType;
        GoalConfig config;

        switch (source.Type)
        {
            case 1 when source.StartingRank is >= 1
                && source.TargetRank.HasValue
                && source.TargetRank.Value > source.StartingRank.Value:
                goalType = GoalType.Rank;
                config = new GoalConfig
                {
                    Rank = new RankTarget
                    {
                        Start = source.StartingRank.Value - 1,
                        StartPointFive = source.StartingRankPoint5 ?? false,
                        StartAppliedUpgrades = source.StartingRankAppliedUpgrades ?? 0,
                        End = source.TargetRank!.Value - 1,
                        EndPointFive = source.RankPoint5 ?? false,
                        EndAppliedUpgrades = source.RankAppliedUpgrades ?? 0,
                    }
                };
                break;
            case 2:
                var start = Progression(source.StartingRarity, source.StartingStars);
                var end = Progression(source.TargetRarity, source.TargetStars);
                if (start is null || end is null)
                {
                    issues.Add(new V1ImportIssue("invalid_progression", source.Id, "The ascension target is not on the V2 progression ladder."));
                    return null;
                }
                goalType = GoalType.Ascension;
                config = new GoalConfig { Progression = new ProgressionTarget { Start = start, End = end } };
                break;
            case 3:
                goalType = GoalType.Unlock;
                config = new GoalConfig();
                break;
            case 4 or 5 when source.FirstAbilityLevel is not null || source.SecondAbilityLevel is not null:
                goalType = GoalType.Ability;
                config = new GoalConfig
                {
                    Ability = new AbilityTarget
                    {
                        ActiveStart = 0,
                        ActiveEnd = source.FirstAbilityLevel ?? 0,
                        PassiveStart = 0,
                        PassiveEnd = source.SecondAbilityLevel ?? 0,
                    }
                };
                break;
            default:
                issues.Add(new V1ImportIssue("malformed_goal", source.Id, "The goal is missing a required target."));
                return null;
        }

        return new TranslatedGoal(new GoalKey(entityType, entityId, goalType), config, source.Priority, source.Notes);
    }

    private static List<TranslatedGoal> CollapseProgressionGoals(List<TranslatedGoal> goals)
    {
        var collapsed = new List<TranslatedGoal>();
        foreach (var group in goals.GroupBy(goal => goal.Key))
        {
            var ordered = group.OrderBy(goal => goal.Priority).ToList();
            if (group.Key.GoalType == GoalType.Rank)
            {
                var ranks = ordered.Select(goal => goal.Config.Rank!).ToList();
                var first = ranks.MinBy(rank => rank.Start)!;
                var last = ranks.MaxBy(rank => rank.End)!;
                collapsed.Add(ordered[0] with
                {
                    Config = new GoalConfig
                    {
                        Rank = new RankTarget
                        {
                            Start = first.Start,
                            StartPointFive = first.StartPointFive,
                            StartAppliedUpgrades = first.StartAppliedUpgrades,
                            End = last.End,
                            EndPointFive = last.EndPointFive,
                            EndAppliedUpgrades = last.EndAppliedUpgrades,
                        }
                    },
                    Notes = JoinNotes(ordered),
                });
            }
            else if (group.Key.GoalType == GoalType.Ascension)
            {
                var first = ordered.MinBy(goal => Array.IndexOf(ProgressionOrder, goal.Config.Progression!.Start))!;
                var last = ordered.MaxBy(goal => Array.IndexOf(ProgressionOrder, goal.Config.Progression!.End))!;
                collapsed.Add(ordered[0] with
                {
                    Config = new GoalConfig
                    {
                        Progression = new ProgressionTarget
                        {
                            Start = first.Config.Progression!.Start,
                            End = last.Config.Progression!.End,
                        }
                    },
                    Notes = JoinNotes(ordered),
                });
            }
            else
            {
                collapsed.AddRange(ordered);
            }
        }

        return collapsed.OrderBy(goal => goal.Priority).ToList();
    }

    private static (Goal Goal, ProjectGoal Link) BuildGoal(
        ProfileId profileId,
        TranslatedGoal translated,
        DateTimeOffset now,
        ProjectId projectId,
        int priority)
    {
        var goal = new Goal
        {
            Id = GoalId.From(Guid.CreateVersion7()),
            ProfileId = profileId,
            EntityType = translated.Key.EntityType,
            EntityId = translated.Key.EntityId,
            GoalType = translated.Key.GoalType,
            Status = GoalStatus.Paused,
            Notes = translated.Notes,
            Config = translated.Config,
            Snapshot = new GoalSnapshot { CreatedAt = now },
            Events = [new GoalEvent { At = now, Type = GoalEventType.Created }],
        };
        goal.Milestones = goal.GoalType switch
        {
            GoalType.Rank => MilestoneGenerator.ForRank(goal.Config.Rank!.Start, goal.Config.Rank.End),
            GoalType.Ascension => MilestoneGenerator.ForProgression(goal.Config.Progression!.Start, goal.Config.Progression.End),
            _ => [],
        };

        return (goal, new ProjectGoal { ProjectId = projectId, GoalId = goal.Id, Priority = priority, CreatedAt = now });
    }

    private static string? Progression(int? rarity, int? stars)
    {
        if (rarity is null || stars is null || rarity < 0 || rarity >= Rarities.Length || stars < 0 || stars >= Stars.Length)
        {
            return null;
        }
        var value = $"{Rarities[rarity.Value]}:{Stars[stars.Value]}";
        return Progressions.Contains(value) ? value : null;
    }

    private static bool Matches(string id, string name, string? source) =>
        !string.IsNullOrWhiteSpace(source)
        && (string.Equals(id, source, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, source, StringComparison.OrdinalIgnoreCase));

    private static string? JoinNotes(IEnumerable<TranslatedGoal> goals)
    {
        var notes = goals.Select(goal => goal.Notes?.Trim()).Where(note => !string.IsNullOrWhiteSpace(note));
        var result = string.Join(Environment.NewLine, notes);
        return result.Length == 0 ? null : result;
    }

    private sealed record GoalKey(GoalEntityType EntityType, string EntityId, GoalType GoalType);
    private sealed record TranslatedGoal(GoalKey Key, GoalConfig Config, int Priority, string? Notes);
}

public sealed record V1GoalImportResult(int Imported, int Replaced, int Skipped, IReadOnlyList<V1ImportIssue> Issues);

public sealed record V1ImportIssue(string Code, string? SourceGoalId, string Message);
