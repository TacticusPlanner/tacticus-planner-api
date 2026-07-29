namespace TacticusPlanner.Domain.Projects;

/// <summary>Persisted as a string; explicit values are future-proofing only.</summary>
public enum ProjectStatus
{
    Active = 1,
    Paused = 2,
    Archived = 3,
}
