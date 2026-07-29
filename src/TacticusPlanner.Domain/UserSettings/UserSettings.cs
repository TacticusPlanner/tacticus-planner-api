using TacticusPlanner.Domain.Common;
using TacticusPlanner.Domain.Profiles;

namespace TacticusPlanner.Domain.UserSettings;

/// <summary>Renamed from <c>PlanningSettings</c> — a home for account-wide user preferences, not only
/// Goals planning knobs. <see cref="Settings"/> is a nested JSON value object (rather than flat columns)
/// so future knobs don't each need their own migration.</summary>
public class UserSettings : BaseEntity<ProfileId>, IRevisionedEntity
{
    public long Revision { get; set; }

    public UserSettingsData Settings { get; set; } = new();

    public virtual Profile? Profile { get; set; }
}

/// <summary>The nested, JSON-serialized settings payload (see <see cref="UserSettings.Settings"/>).</summary>
public sealed class UserSettingsData
{
    public const int DefaultDailyEnergy = 288;

    public static readonly IReadOnlySet<int> SupportedDailyEnergy =
        new HashSet<int> { 288, 378, 438, 538, 638, 738, 838, 938 };

    public int DailyEnergy { get; set; } = DefaultDailyEnergy;
}
