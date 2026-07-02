using TacticusPlanner.Api.Persistence.Encryption;
using TacticusPlanner.Api.Persistence.Model;

namespace TacticusPlanner.Api.Persistence.Users;

public class TacticusIntegration : BaseEntity<ProfileId>
{
    [Encrypted]
    public string? TacticusApiKey { get; set; }

    public DateTimeOffset? TacticusSyncLastAttemptedAt { get; set; }

    public DateTimeOffset? TacticusSyncLastSucceededAt { get; set; }

    public string? TacticusSyncLastResultCode { get; set; }

    public DateTimeOffset? TacticusSourceUpdatedAt { get; set; }

    public virtual Profile? Profile { get; set; }
}
