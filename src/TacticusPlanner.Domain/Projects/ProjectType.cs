namespace TacticusPlanner.Domain.Projects;

/// <summary>Replaces the former <c>Project.IsDefault</c> bool. Persisted as a string; explicit values are
/// future-proofing only.</summary>
public enum ProjectType
{
    Custom = 1,

    /// <summary>The profile's auto-provisioned project ("My Goals") — informational only; it can be
    /// renamed but cannot be archived. At most one per profile (see
    /// <c>ProjectsService.EnsureDefaultProjectAsync</c>).</summary>
    Default = 2,
}
