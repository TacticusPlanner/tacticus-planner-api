using TacticusPlanner.Api.Features.UserSettings;

namespace TacticusPlanner.Api.Tests;

public sealed class UserPreferenceFlagsTests
{
    [Fact]
    public void ReadFlagsReadsNullAsNone()
    {
        Assert.Empty(UserSettingsResponse.ReadFlags(null));
    }

    [Fact]
    public void ReadFlagsDropsUnknownNamesAndDuplicates()
    {
        var flags = UserSettingsResponse.ReadFlags(
            ["Removed", "LegendaryEventShowPaidOptions", "LegendaryEventShowPaidOptions", "0"]);

        Assert.Equal([UserPreference.LegendaryEventShowPaidOptions], flags);
    }

    [Fact]
    public void WriteFlagsStoresEachNameOnce()
    {
        var stored = UserSettingsResponse.WriteFlags(
            [UserPreference.LegendaryEventShowPaidOptions, UserPreference.LegendaryEventShowPaidOptions]);

        Assert.Equal(["LegendaryEventShowPaidOptions"], stored);
    }
}
