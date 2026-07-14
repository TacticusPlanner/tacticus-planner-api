using TacticusPlanner.Domain.Goals;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Api.Features.Goals;

public static class GoalQueries
{
    public static IQueryable<Goal> Owned(this IQueryable<Goal> goals, ProfileId profileId) =>
        goals.Where(goal => goal.ProfileId == profileId);
}
