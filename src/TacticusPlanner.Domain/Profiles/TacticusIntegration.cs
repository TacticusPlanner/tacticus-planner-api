using TacticusPlanner.Domain.Common;

namespace TacticusPlanner.Domain.Profiles;

public class TacticusIntegration : BaseEntity<ProfileId>
{
    public string? TacticusApiKey { get; set; }

    public DateTimeOffset? TacticusSyncLastAttemptedAt { get; set; }

    public DateTimeOffset? TacticusSyncLastSucceededAt { get; set; }

    public virtual Profile? Profile { get; set; }
}
