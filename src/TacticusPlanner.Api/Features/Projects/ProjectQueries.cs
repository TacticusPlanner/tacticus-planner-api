using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Api.Features.Projects;

public static class ProjectQueries
{
    public static IQueryable<Project> Owned(this IQueryable<Project> projects, ProfileId profileId) =>
        projects.Where(project => project.ProfileId == profileId);
}
