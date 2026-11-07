using TacticusPlanner.Api.Features.UserSettings;
using TacticusPlanner.Domain.UserSettings;
using UserSettingsResponse = TacticusPlanner.Api.Features.UserSettings.UserSettingsResponse;

namespace TacticusPlanner.Api.Tests;

public sealed class UserPreferencesTests
{
    [Fact]
    public void EveryPreferenceHasItsOwnBit()
    {
        var preferences = Enum.GetValues<UserPreference>();

        var flags = preferences.Select(preference => UserSettingsResponse.ToFlags([preference])).ToList();

        Assert.All(flags, flag => Assert.True(flag != UserPreferences.None && (flag & (flag - 1)) == 0));
        Assert.Equal(flags.Count, flags.Distinct().Count());
        Assert.Equal(preferences, UserSettingsResponse.ToList(UserSettingsResponse.ToFlags(preferences)));
    }

    [Fact]
    public void ToListDropsBitsNoPreferenceNames()
    {
        var flags = UserPreferences.LegendaryEventSkipDailyAds | (UserPreferences)(1L << 40);

        Assert.Equal([UserPreference.LegendaryEventSkipDailyAds], UserSettingsResponse.ToList(flags));
    }

    [Fact]
    public void LegacyWatchAdsOffFoldsIntoTheSkipFlag()
    {
        Assert.Equal(
            UserPreferences.LegendaryEventSkipDailyAds,
            new UserSettingsData { WatchAds = false }.EffectivePreferences);
        Assert.Equal(UserPreferences.None, new UserSettingsData { WatchAds = true }.EffectivePreferences);
        Assert.Equal(UserPreferences.None, new UserSettingsData().EffectivePreferences);
    }
}
