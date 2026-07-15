using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.Planning;

public class PlanningSettings : BaseEntity<ProfileId>, IRevisionedEntity
{
    public const int DefaultDailyEnergy = 288;

    public static readonly IReadOnlySet<int> SupportedDailyEnergy =
        new HashSet<int> { 288, 378, 438, 538, 638, 738, 838, 938 };

    public long Revision { get; set; }

    public int DailyEnergy { get; set; } = DefaultDailyEnergy;

    public PlanningOrdering Ordering { get; set; } = PlanningOrdering.GoalPriority;

    public virtual Profile? Profile { get; set; }
}
