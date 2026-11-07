using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;
using UserPreferences = TacticusPlanner.Domain.UserSettings.UserPreferences;
using UserSettingsData = TacticusPlanner.Domain.UserSettings.UserSettingsData;
using UserSettingsEntity = TacticusPlanner.Domain.UserSettings.UserSettings;

namespace TacticusPlanner.Api.Features.UserSettings;

public sealed class GetUserSettingsEndpoint : EndpointWithoutRequest<UserSettingsResponse>
{
    public override void Configure()
    {
        Get("settings");
        Summary(summary => summary.Summary = "Gets the current profile's user settings.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var profileIdValue = profileId.Value;
        var db = Resolve<PlannerDbContext>();
        var settings = await db.UserSettings.FirstOrDefaultAsync(entity => entity.Id == profileIdValue, ct);
        if (settings is null)
        {
            settings = new UserSettingsEntity { Id = profileIdValue };
            db.UserSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        await Send.OkAsync(UserSettingsResponse.From(settings), ct);
    }
}

/// <summary>The rarity of XP book the planner spends; stored by name.</summary>
public enum XpBookRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
    Mythic,
}

/// <summary>One on/off account preference (a <see cref="UserPreferences"/> flag); listed in
/// <see cref="UserSettingsResponse.Preferences"/> when it is on.</summary>
public enum UserPreference
{
    /// <inheritdoc cref="UserPreferences.LegendaryEventShowPaidOptions"/>
    LegendaryEventShowPaidOptions,

    /// <inheritdoc cref="UserPreferences.LegendaryEventSkipDailyAds"/>
    LegendaryEventSkipDailyAds,
}

/// <param name="Preferences">The account preferences that are on, each once, in declaration order (default
/// none).</param>
public sealed record UserSettingsResponse(
    int DailyEnergy,
    XpBookRarity XpBookRarity,
    UserPreference[] Preferences,
    long Revision)
{
    public static UserSettingsResponse From(UserSettingsEntity settings) =>
        new(
            settings.Settings.DailyEnergy,
            NormalizeXpBookRarity(settings.Settings.XpBookRarity),
            ToList(settings.Settings.EffectivePreferences),
            settings.Revision);

    private static readonly (UserPreference Preference, UserPreferences Flag)[] Bits =
    [
        (UserPreference.LegendaryEventShowPaidOptions, UserPreferences.LegendaryEventShowPaidOptions),
        (UserPreference.LegendaryEventSkipDailyAds, UserPreferences.LegendaryEventSkipDailyAds),
    ];

    /// <summary>The bitmask's preferences in declaration order; a bit no preference names is dropped.</summary>
    public static UserPreference[] ToList(UserPreferences flags) =>
        Bits.Where(bit => flags.HasFlag(bit.Flag)).Select(bit => bit.Preference).ToArray();

    /// <summary>The listed preferences as one bitmask (duplicates collapse).</summary>
    public static UserPreferences ToFlags(IEnumerable<UserPreference> preferences) =>
        preferences.Aggregate(
            UserPreferences.None,
            (flags, preference) => flags | Bits.Single(bit => bit.Preference == preference).Flag);

    // A row saved before this field existed (or any other missing/null/unsupported stored value) reads
    // as the default rather than leaking an invalid value to the client.
    private static XpBookRarity NormalizeXpBookRarity(string? xpBookRarity) =>
        Enum.TryParse<XpBookRarity>(xpBookRarity ?? UserSettingsData.DefaultXpBookRarity, out var rarity)
            && Enum.IsDefined(rarity)
            ? rarity
            : Enum.Parse<XpBookRarity>(UserSettingsData.DefaultXpBookRarity);
}
