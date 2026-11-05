using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Persistence;
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

/// <summary>An on/off account preference; a flag listed in <see cref="UserSettingsResponse.Flags"/> is on.
/// New preferences are added here, so they need no new field.</summary>
public enum UserPreference
{
    /// <summary>Legendary Event planners include paid options (premium missions, the event bundle and bought
    /// shards) in every event.</summary>
    LegendaryEventShowPaidOptions,
}

/// <param name="WatchAds">The player watches the daily Legendary Event ad token (default true).</param>
/// <param name="Flags">The account preferences that are on, each once, in declaration order (default none).</param>
public sealed record UserSettingsResponse(
    int DailyEnergy,
    XpBookRarity XpBookRarity,
    bool WatchAds,
    UserPreference[] Flags,
    long Revision)
{
    public static UserSettingsResponse From(UserSettingsEntity settings) =>
        new(
            settings.Settings.DailyEnergy,
            NormalizeXpBookRarity(settings.Settings.XpBookRarity),
            settings.Settings.WatchAds ?? true,
            ReadFlags(settings.Settings.Flags),
            settings.Revision);

    /// <summary>Stored flag names to flags: unknown names (a flag since removed) are dropped, duplicates
    /// collapse.</summary>
    public static UserPreference[] ReadFlags(IEnumerable<string>? stored) =>
        (stored ?? [])
            .Select(name => Enum.TryParse<UserPreference>(name, out var flag) && Enum.IsDefined(flag)
                ? flag
                : (UserPreference?)null)
            .OfType<UserPreference>()
            .Distinct()
            .Order()
            .ToArray();

    /// <summary>Flags to their stored names, each once in declaration order.</summary>
    public static List<string> WriteFlags(IEnumerable<UserPreference> flags) =>
        flags.Distinct().Order().Select(flag => flag.ToString()).ToList();

    // A row saved before this field existed (or any other missing/null/unsupported stored value) reads
    // as the default rather than leaking an invalid value to the client.
    private static XpBookRarity NormalizeXpBookRarity(string? xpBookRarity) =>
        Enum.TryParse<XpBookRarity>(xpBookRarity ?? UserSettingsData.DefaultXpBookRarity, out var rarity)
            && Enum.IsDefined(rarity)
            ? rarity
            : Enum.Parse<XpBookRarity>(UserSettingsData.DefaultXpBookRarity);
}
