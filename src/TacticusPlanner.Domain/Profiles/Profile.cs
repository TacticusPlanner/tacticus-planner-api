using TacticusPlanner.Domain.Accounts;
using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Projects;

namespace TacticusPlanner.Domain.Profiles;

public class Profile : BaseEntity<ProfileId>
{
    public AccountId AccountId { get; set; }

    public required string DisplayName { get; set; }

    public TacticusUserId? TacticusUserId { get; set; }

    public byte[]? TacticusUserIdHash { get; set; }

    /// <summary>The profile's current active plan (plan §3.2/§5) — a loose id (no DB FK, to avoid a
    /// Profile↔Project cascade cycle), set to a project's id on activation. A single nullable pointer
    /// here structurally guarantees at most one active plan per profile, unlike a per-project boolean
    /// flag with a partial unique index.</summary>
    public ProjectId? ActiveProjectId { get; set; }

    public virtual Account? Account { get; set; }

    public virtual TacticusIntegration? TacticusIntegration { get; set; }

    public virtual PlayerDataSnapshot? PlayerDataSnapshot { get; set; }

    public virtual PlayerDataOverride? PlayerDataOverride { get; set; }
}
