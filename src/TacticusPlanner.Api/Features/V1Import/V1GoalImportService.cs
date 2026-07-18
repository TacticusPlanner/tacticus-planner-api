using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// Translates V1 goals into V2 create-request specs — this service does not persist any goals itself.
/// V1 only ever supplies target/end values (V1 has no notion of a tracked starting point beyond the
/// character's state at goal-creation time, which is exactly what "current" means here too); every
/// goal's starting point is read fresh from the account's live <see cref="PlayerDataSnapshot"/> instead
/// of whatever stale starting values the V1 record happened to carry. The account's existing
/// (non-deleted) goals are also read, to skip candidates that already have a matching
/// (EntityType, EntityId, GoalType) goal. Each returned spec's <c>Snapshot</c> is left null — same as
/// the goal's Start values are read from live player data rather than V1, the initial-state snapshot
/// is resolved client-side, by the same <c>buildCreateGoalSnapshot</c> the regular create-goal flow
/// uses, once the client has this spec plus its own live player data and estimate engine to build it
/// against. The result is handed back to <see cref="ImportV1ProfileEndpoint"/> as
/// <see cref="CreateCombinedGoalsRequest"/> specs — one per unit — for the client to submit through the
/// same <c>POST me/goals/combined</c> endpoint the regular create-goal flow uses. Keeping goal creation
/// entirely client-side avoids a second, server-only creation path.
/// </summary>
public sealed class V1GoalImportService(PlannerDbContext db, IGameCatalogProvider catalog)
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

    public async Task<V1GoalImportResult> TranslateAsync(ProfileId profileId, IReadOnlyList<V1Goal> source, CancellationToken ct)
    {
        var issues = new List<V1ImportIssue>();
        var playerSnapshot = await db.PlayerDataSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == profileId, ct);
        var translated = source
            .OrderBy(goal => goal.Priority)
            .Select(goal => Translate(goal, playerSnapshot, issues))
            .Where(goal => goal is not null)
            .Cast<TranslatedGoal>()
            .ToList();

        var skipped = source.Count - translated.Count;
        var candidates = CollapseProgressionGoals(translated);
        if (candidates.Count == 0)
        {
            return new V1GoalImportResult([], skipped, issues);
        }

        var existingKeys = await db.Goals
            .Where(goal => goal.ProfileId == profileId && goal.Status != GoalStatus.Deleted)
            .Select(goal => new { goal.EntityType, goal.EntityId, goal.GoalType })
            .ToListAsync(ct);
        var existing = existingKeys.Select(key => new GoalKey(key.EntityType, key.EntityId, key.GoalType)).ToHashSet();

        var creatable = new List<TranslatedGoal>();
        foreach (var candidate in candidates)
        {
            if (existing.Contains(candidate.Key))
            {
                skipped++;
                issues.Add(new V1ImportIssue(
                    "goal_already_exists", candidate.SourceId, "A goal of this type already exists for this entity."));
            }
            else
            {
                creatable.Add(candidate);
            }
        }

        // Snapshot is deliberately left null here — same as every other field this service doesn't
        // build (it's the client's job, via the same buildCreateGoalSnapshot the regular create-goal
        // flow uses once it has this spec plus live player data to resolve it against).
        var specs = creatable
            .GroupBy(candidate => (candidate.Key.EntityType, candidate.Key.EntityId))
            .Select(group => new CreateCombinedGoalsRequest(
                group.Key.EntityType.ToString(),
                group.Key.EntityId,
                null,
                group.Select(candidate => new CombinedGoalSpec(
                    candidate.Key.GoalType.ToString(),
                    candidate.Config,
                    [],
                    null
                )).ToList()
            ))
            .ToList();

        return new V1GoalImportResult(specs, skipped, issues);
    }

    private TranslatedGoal? Translate(V1Goal source, PlayerDataSnapshot? playerSnapshot, List<V1ImportIssue> issues)
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
        var playerCharacter = isMow ? null : ResolvePlayerCharacter(entityId, playerSnapshot);
        var playerMow = isMow ? ResolvePlayerMow(entityId, playerSnapshot) : null;
        var playerUnit = (PlayerBaseUnitRecord?)playerCharacter ?? playerMow;
        GoalType goalType;
        CreateGoalConfigRequest config;

        switch (source.Type)
        {
            case 1 when source.TargetRank.HasValue:
                goalType = GoalType.Rank;
                var currentRank = (int)(playerCharacter?.Rank ?? UnitRank.Stone1);
                var targetRank = source.TargetRank.Value - 1;
                if (targetRank <= currentRank)
                {
                    issues.Add(new V1ImportIssue(
                        "rank_target_already_reached", source.Id, "The target rank is at or below the character's current rank."));
                    return null;
                }
                config = new CreateGoalConfigRequest(
                    Rank: new RankTargetRequest(
                        currentRank,
                        false,
                        0,
                        targetRank,
                        source.RankPoint5 ?? false,
                        source.RankAppliedUpgrades ?? 0
                    )
                );
                break;
            case 2:
                var currentProgression = CurrentProgression(playerUnit);
                var end = Progression(source.TargetRarity, source.TargetStars);
                if (end is null)
                {
                    issues.Add(new V1ImportIssue("invalid_progression", source.Id, "The ascension target is not on the V2 progression ladder."));
                    return null;
                }
                if (Array.IndexOf(ProgressionOrder, end) <= Array.IndexOf(ProgressionOrder, currentProgression))
                {
                    issues.Add(new V1ImportIssue(
                        "ascension_target_already_reached", source.Id, "The target progression is at or below the unit's current progression."));
                    return null;
                }
                goalType = GoalType.Ascension;
                config = new CreateGoalConfigRequest(Progression: new ProgressionTargetRequest(currentProgression, end));
                break;
            case 3:
                if (playerUnit is not null)
                {
                    issues.Add(new V1ImportIssue("already_unlocked", source.Id, "The character is already unlocked."));
                    return null;
                }
                goalType = GoalType.Unlock;
                config = new CreateGoalConfigRequest();
                break;
            case 4 or 5 when source.FirstAbilityLevel is not null || source.SecondAbilityLevel is not null:
                var activeStart = playerUnit?.Abilities.ElementAtOrDefault(0)?.Level ?? 0;
                var passiveStart = playerUnit?.Abilities.ElementAtOrDefault(1)?.Level ?? 0;
                var activeEnd = source.FirstAbilityLevel ?? 0;
                var passiveEnd = source.SecondAbilityLevel ?? 0;
                if (activeEnd <= activeStart && passiveEnd <= passiveStart)
                {
                    issues.Add(new V1ImportIssue(
                        "ability_target_already_reached", source.Id, "The ability targets are at or below the unit's current levels."));
                    return null;
                }
                goalType = GoalType.Ability;
                config = new CreateGoalConfigRequest(
                    Ability: new AbilityTargetRequest(activeStart, activeEnd, passiveStart, passiveEnd)
                );
                break;
            default:
                issues.Add(new V1ImportIssue("malformed_goal", source.Id, "The goal is missing a required target."));
                return null;
        }

        return new TranslatedGoal(new GoalKey(entityType, entityId, goalType), config, source.Priority, source.Notes, source.Id);
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
                    Config = new CreateGoalConfigRequest(
                        Rank: new RankTargetRequest(
                            first.Start,
                            first.StartPointFive,
                            first.StartAppliedUpgrades,
                            last.End,
                            last.EndPointFive,
                            last.EndAppliedUpgrades
                        )
                    ),
                    Notes = JoinNotes(ordered),
                });
            }
            else if (group.Key.GoalType == GoalType.Ascension)
            {
                var first = ordered.MinBy(goal => Array.IndexOf(ProgressionOrder, goal.Config.Progression!.Start))!;
                var last = ordered.MaxBy(goal => Array.IndexOf(ProgressionOrder, goal.Config.Progression!.End))!;
                collapsed.Add(ordered[0] with
                {
                    Config = new CreateGoalConfigRequest(
                        Progression: new ProgressionTargetRequest(
                            first.Config.Progression!.Start,
                            last.Config.Progression!.End
                        )
                    ),
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

    private static string? Progression(int? rarity, int? stars)
    {
        if (rarity is null || stars is null || rarity < 0 || rarity >= Rarities.Length || stars < 0 || stars >= Stars.Length)
        {
            return null;
        }
        var value = $"{Rarities[rarity.Value]}:{Stars[stars.Value]}";
        return Progressions.Contains(value) ? value : null;
    }

    /// <summary>The unit's live progression as a "Rarity:Stars" key — the same wire format
    /// <see cref="ProgressionOrder"/> and <see cref="ProgressionTargetRequest"/> use. Defaults to the
    /// bottom of the ladder when the account has no synced player data for this unit yet.</summary>
    private static string CurrentProgression(PlayerBaseUnitRecord? playerUnit)
    {
        var index = playerUnit is null ? 0 : (int)playerUnit.ProgressionIndex;
        return index >= 0 && index < ProgressionOrder.Length ? ProgressionOrder[index] : ProgressionOrder[0];
    }

    private static PlayerCharacterRecord? ResolvePlayerCharacter(string entityId, PlayerDataSnapshot? playerSnapshot) =>
        playerSnapshot?.Characters.FirstOrDefault(item => item.UnitId.Value == entityId);

    private static PlayerMowRecord? ResolvePlayerMow(string entityId, PlayerDataSnapshot? playerSnapshot) =>
        playerSnapshot?.Mows.FirstOrDefault(item => item.UnitId.Value == entityId);

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
    private sealed record TranslatedGoal(GoalKey Key, CreateGoalConfigRequest Config, int Priority, string? Notes, string? SourceId);
}

public sealed record V1GoalImportResult(
    IReadOnlyList<CreateCombinedGoalsRequest> GoalSpecs,
    int Skipped,
    IReadOnlyList<V1ImportIssue> Issues
);

public sealed record V1ImportIssue(string Code, string? SourceGoalId, string Message);
