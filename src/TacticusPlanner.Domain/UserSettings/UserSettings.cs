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
    public const string DefaultXpBookRarity = "Legendary";

    public static readonly IReadOnlySet<int> SupportedDailyEnergy =
        new HashSet<int> { 288, 378, 438, 538, 638, 738, 838, 938 };

    public static readonly IReadOnlySet<string> SupportedXpBookRarity =
        new HashSet<string> { "Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic" };

    public int DailyEnergy { get; set; } = DefaultDailyEnergy;

    public string XpBookRarity { get; set; } = DefaultXpBookRarity;

    /// <summary>Legacy: whether the player watches the daily Legendary Event ad, from before
    /// <see cref="Preferences"/>. Only read (false reads as <see cref="UserPreferences.LegendaryEventSkipDailyAds"/>);
    /// every write clears it.</summary>
    public bool? WatchAds { get; set; }

    /// <summary>On/off account preferences as one bitmask (a row saved before this field existed reads as
    /// <see cref="UserPreferences.None"/>).</summary>
    public UserPreferences Preferences { get; set; }

    /// <summary>The preferences with the legacy <see cref="WatchAds"/> folded in.</summary>
    public UserPreferences EffectivePreferences =>
        WatchAds == false ? Preferences | UserPreferences.LegendaryEventSkipDailyAds : Preferences;
}

/// <summary>On/off account preferences, stored as a bitmask. Every flag is off by default, so a preference
/// whose default is "on" is named for its opposite (<see cref="LegendaryEventSkipDailyAds"/>). Add a flag with
/// the next free bit; never reuse or renumber one.</summary>
[Flags]
public enum UserPreferences : long
{
    None = 0,

    /// <summary>Legendary Event planners include paid options (premium missions, the event bundle and bought
    /// shards) in every event.</summary>
    LegendaryEventShowPaidOptions = 1L << 0,

    /// <summary>The player doesn't watch the daily ad, so Legendary Event plans leave out the ad token.</summary>
    LegendaryEventSkipDailyAds = 1L << 1,
}
