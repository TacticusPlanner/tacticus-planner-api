namespace TacticusPlanner.Domain.Projects;

/// <summary>Replaces the former <c>Project.IsDefault</c> bool. Persisted as a string; explicit values are
/// future-proofing only.</summary>
public enum ProjectType
{
    Custom = 1,

    /// <summary>The profile's auto-provisioned project ("My Goals") — informational only; it can be
    /// renamed but cannot be archived. At most one per profile (partial unique
    /// index <c>ix_projects_profile_id_default</c>).</summary>
    Default = 2,
}
