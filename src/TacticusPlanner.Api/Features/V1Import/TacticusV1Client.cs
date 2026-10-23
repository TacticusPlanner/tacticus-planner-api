using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TacticusPlanner.Api.Features.V1Import;

/// <summary>
/// Talks to the legacy V1 planner backend to acquire a short-lived V1 access token from a username/password
/// and read the V1 profile's Tacticus integration fields. The V1 credentials never leave this call — only the
/// access token and the resulting profile fields are handed back to the caller.
/// </summary>
public interface ITacticusV1Client
{
    Task<string?> LoginAsync(string username, string password, CancellationToken cancellationToken);

    Task<TacticusV1Profile?> GetProfileAsync(string accessToken, CancellationToken cancellationToken);
}

public sealed record TacticusV1Profile(
    string? TacticusApiKey,
    string? TacticusUserId,
    string? GuildApiKey,
    IReadOnlyList<V1Goal> Goals,
    V1OnslaughtImportData OnslaughtProgress,
    V1CampaignEventProgressImportData CampaignEventProgress
)
{
    public TacticusV1Profile(string? tacticusApiKey, string? tacticusUserId)
        : this(tacticusApiKey, tacticusUserId, null, [], V1OnslaughtImportData.Missing(),
            V1CampaignEventProgressImportData.Missing())
    {
    }

    /// <summary>V1 Legendary Event teams, progress and settings (<c>leTeams</c>/<c>legendaryEvents3</c>,
    /// <c>leProgress</c>/<c>legendaryEventsProgress</c>, <c>leSettings</c>); missing unless the profile carries
    /// them.</summary>
    public V1LegendaryEventImportData LegendaryEvents { get; init; } = V1LegendaryEventImportData.Missing();
}

/// <summary>Distinguishes a profile with no V1 Legendary Event teams or progress from one whose teams cannot be
/// read; the import reports those as Skipped and Failed. Events are in ascending V1 enum order.</summary>
/// <param name="ShowPaidOptions">V1 <c>leSettings.showP2POptions</c> when it is a boolean, else null.</param>
public sealed record V1LegendaryEventImportData(
    bool IsPresent,
    IReadOnlyList<V1LegendaryEventSource>? Events,
    bool? ShowPaidOptions = null)
{
    public static V1LegendaryEventImportData Missing() => new(false, null);

    public static V1LegendaryEventImportData Invalid() => new(true, null);

    public static V1LegendaryEventImportData Valid(IReadOnlyList<V1LegendaryEventSource> events, bool? showPaidOptions = null) =>
        new(true, events, showPaidOptions);
}

/// <summary>One V1 event's teams (V1 <c>ILegendaryEventSelectedTeams</c>, null when the event is only in the
/// progress key) and what this import reads from its V1 progress.</summary>
/// <param name="V1EventId">V1's numeric <c>LegendaryEventEnum</c> key.</param>
/// <param name="Progress">The event's V1 progress, or null when absent or unreadable.</param>
public sealed record V1LegendaryEventSource(
    int V1EventId,
    V1LegendaryEventTeams? Teams,
    string? Notes,
    V1LreProgress? Progress = null);

public sealed record V1LegendaryEventTeams(
    int? Id = null,
    string? Name = null,
    List<V1LreTeam>? Teams = null,
    Dictionary<string, List<string>?>? Alpha = null,
    Dictionary<string, List<string>?>? Beta = null,
    Dictionary<string, List<string>?>? Gamma = null
);

/// <summary>V1 <c>ILreTeam</c>. Units are <see cref="CharSnowprintIds"/>, else legacy character names in
/// <see cref="CharactersIds"/>, else embedded <see cref="Characters"/>; objectives are V1 display names.</summary>
public sealed record V1LreTeam(
    string? Id = null,
    string? Name = null,
    string? Section = null,
    List<string>? RestrictionsIds = null,
    List<string>? CharSnowprintIds = null,
    List<string>? CharactersIds = null,
    List<V1LreTeamCharacter>? Characters = null,
    double? ExpectedBattleClears = null
);

public sealed record V1LreTeamCharacter(string? SnowprintId);

/// <summary>The fields of V1 <c>ILreProgressDto</c> this import reads: notes, the per-run <c>overview</c> (keys
/// <c>1</c>–<c>3</c>) and the Maybe clear / Stop here statuses in <c>compactProgress</c> (lane → requirement id →
/// statuses by battle index) or, on older profiles, <c>battlesProgress</c>. Numbers stay raw because V1 stores
/// them loosely.</summary>
public sealed record V1LreProgress(
    string? Notes = null,
    Dictionary<string, V1LreOccurrence?>? Overview = null,
    Dictionary<string, Dictionary<string, V1LreCompactRequirement?>?>? CompactProgress = null,
    List<V1LreBattleProgress?>? BattlesProgress = null
);

/// <summary>V1 <c>ILreOccurrenceProgress</c>: one run's missions, bundle (0/1) and "Oh So Close" shards.</summary>
public sealed record V1LreOccurrence(
    JsonElement? RegularMissions = null,
    JsonElement? PremiumMissions = null,
    JsonElement? Bundle = null,
    JsonElement? OhSoCloseShards = null
);

/// <summary>A V1 compact requirement: <see cref="Statuses"/> maps a battle index to a <c>RequirementStatus</c> and
/// <see cref="States"/> to a <c>ProgressState</c> (each an object keyed by index, or an array by position). A
/// battle with no status and state 2 (blocked) reads as Stop here, as V1 reads it.</summary>
public sealed record V1LreCompactRequirement(JsonElement? Statuses = null, JsonElement? States = null);

/// <summary>V1's legacy per-battle progress.</summary>
public sealed record V1LreBattleProgress(
    string? TrackId = null,
    JsonElement? BattleIndex = null,
    List<V1LreRequirementProgress?>? Requirements = null
);

/// <summary>A legacy requirement: <see cref="State"/> 2 (blocked) without a <see cref="Status"/> reads as Stop
/// here, as V1 reads it.</summary>
public sealed record V1LreRequirementProgress(
    string? Id = null,
    JsonElement? State = null,
    JsonElement? Status = null
);

public sealed record V1CampaignEventProgress(string CampaignGroupId, string Type, int CompletedBattleCount);

public sealed record V1CampaignEventProgressImportData(
    bool IsPresent,
    IReadOnlyList<V1CampaignEventProgress>? Progress)
{
    public static V1CampaignEventProgressImportData Missing() => new(false, null);

    public static V1CampaignEventProgressImportData Invalid() => new(true, null);

    public static V1CampaignEventProgressImportData Valid(IReadOnlyList<V1CampaignEventProgress> progress) =>
        new(true, progress);
}

public sealed record V1OnslaughtAllianceProgress(string Sector, int Tier);

public sealed record V1OnslaughtProgress(
    V1OnslaughtAllianceProgress Imperial,
    V1OnslaughtAllianceProgress Xenos,
    V1OnslaughtAllianceProgress Chaos
);

/// <summary>Distinguishes a profile with no legacy Onslaught field from one whose field exists but
/// cannot be parsed. The import endpoint reports those cases as Skipped and Failed respectively.</summary>
public sealed record V1OnslaughtImportData(bool IsPresent, V1OnslaughtProgress? Progress)
{
    public static V1OnslaughtImportData Missing() => new(false, null);

    public static V1OnslaughtImportData Invalid() => new(true, null);

    public static V1OnslaughtImportData Valid(V1OnslaughtProgress progress) => new(true, progress);
}

/// <summary>The V1 wire shape's shard-farming choice for an Ascension goal — one of V1's
/// <c>ShardFarmType</c> string-union values (<c>"onslaught"</c>, <c>"energy"</c>, or <c>"both"</c>).
/// Not a C# enum: it's read from a legacy JSON blob and only ever compared, never round-tripped.</summary>
public static class V1ShardFarmType
{
    public const string Onslaught = "onslaught";
    public const string Energy = "energy";
    public const string Both = "both";
}

public sealed record V1Goal(
    string? Id,
    string? Character,
    int Type,
    int Priority,
    // V1's per-goal "include this in Daily Raids" flag — the only activation signal a V1 goal carries,
    // and the direct analogue of V2's Active/Paused status. Nullable so a V1 record written before the
    // field existed imports as Active rather than being silently paused (goal-lifecycle-status).
    bool? DailyRaids,
    string? Notes,
    int? StartingRank,
    bool? StartingRankPoint5,
    int? StartingRankAppliedUpgrades,
    int? TargetRank,
    bool? RankPoint5,
    int? RankAppliedUpgrades,
    int? StartingRarity,
    int? StartingStars,
    int? TargetRarity,
    int? TargetStars,
    string? UnitId,
    int? FirstAbilityLevel,
    int? SecondAbilityLevel,
    // V1's shard-source choice, previously dropped entirely (rewrite-v1-goal-import) — present on
    // Unlock (CampaignsUsage only) and Ascension (all three) goals. V1's CampaignsLocationsUsage is a
    // numeric enum (None = 0, BestTime = 1, LeastEnergy = 2); only "did the player farm campaigns at
    // all" (non-zero) matters here, not which strategy.
    string? ShardFarmType = null,
    int? CampaignsUsage = null,
    int? MythicCampaignsUsage = null,
    // V1's material goals: type 6 ("farm N of one upgrade material", no unit) carries the material and
    // quantity; type 7 (pre-farm) carries the material and the V1 ids of the goals it pre-farms for.
    string? UpgradeMaterialId = null,
    int? UpgradeMaterialQuantity = null,
    IReadOnlyList<string>? PreFarmGoalIds = null
);

public sealed class TacticusV1Client(IHttpClientFactory httpClientFactory) : ITacticusV1Client
{
    public const string HttpClientName = "TacticusV1";
    internal static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string?> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var response = await client.PostAsJsonAsync(
            "api/LoginUser",
            new V1LoginRequest(username, password),
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<V1LoginResponse>(cancellationToken);

        return string.IsNullOrWhiteSpace(payload?.AccessToken) ? null : payload.AccessToken;
    }

    public async Task<TacticusV1Profile?> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        // The whole envelope — including the nested "data" object — is deserialized once here, directly
        // into the typed V1UserDataResponse/V1UserData graph below; nothing downstream re-parses raw JSON.
        var payload = await response.Content.ReadFromJsonAsync<V1UserDataResponse>(WebJsonOptions, cancellationToken);

        if (payload is null)
        {
            return null;
        }

        return new TacticusV1Profile(
            string.IsNullOrWhiteSpace(payload.TacticusApiKey) ? null : payload.TacticusApiKey,
            string.IsNullOrWhiteSpace(payload.TacticusUserId) ? null : payload.TacticusUserId,
            string.IsNullOrWhiteSpace(payload.TacticusGuildApiKey) ? null : payload.TacticusGuildApiKey,
            ReadGoals(payload.Data),
            ReadOnslaughtProgress(payload.Data),
            ReadCampaignEventProgress(payload.Data)
        )
        {
            LegendaryEvents = ReadLegendaryEvents(payload.Data),
        };
    }

    /// <summary>Prefers the current keys and falls back to the pre-V2-schema ones V1 only rewrites on save.</summary>
    internal static V1LegendaryEventImportData ReadLegendaryEvents(V1UserData? data) =>
        ReadLegendaryEvents(
            Present(data?.LeTeams) ?? Present(data?.LegendaryEvents3),
            Present(data?.LeProgress) ?? Present(data?.LegendaryEventsProgress),
            Present(data?.LeSettings));

    private static JsonElement? Present(JsonElement? element) =>
        element is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } ? element : null;

    /// <summary>Reads V1 Legendary Event teams, progress and the paid-options setting. The blobs stay raw
    /// <see cref="JsonElement"/>s on <see cref="V1UserData"/> so a malformed LRE blob fails only this part, not the
    /// whole profile read. Events are the union of the teams and progress keys; an unreadable progress entry
    /// only drops that event's notes, run inputs and marks.</summary>
    /// <param name="teams"><c>leTeams</c>, or the older <c>legendaryEvents3</c> when <c>leTeams</c> is absent.</param>
    /// <param name="progress"><c>leProgress</c>, or the older <c>legendaryEventsProgress</c>.</param>
    /// <param name="settings"><c>leSettings</c>; only <c>showP2POptions</c> is read.</param>
    internal static V1LegendaryEventImportData ReadLegendaryEvents(
        JsonElement? teams, JsonElement? progress, JsonElement? settings = null)
    {
        var hasTeams = teams is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
        var hasProgress = progress is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
        if (!hasTeams && !hasProgress)
        {
            return V1LegendaryEventImportData.Missing();
        }

        try
        {
            var teamsByEvent = hasTeams
                ? teams!.Value.Deserialize<Dictionary<string, V1LegendaryEventTeams?>>(WebJsonOptions) ?? []
                : [];
            var progressByEvent = ReadProgress(progress);

            var eventIds = new SortedSet<int>();
            foreach (var (key, value) in teamsByEvent)
            {
                if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var v1EventId))
                {
                    return V1LegendaryEventImportData.Invalid();
                }

                if (value is not null)
                {
                    eventIds.Add(v1EventId);
                }
            }

            foreach (var key in progressByEvent.Keys)
            {
                eventIds.Add(key);
            }

            var events = new List<V1LegendaryEventSource>();
            foreach (var v1EventId in eventIds)
            {
                var key = v1EventId.ToString(CultureInfo.InvariantCulture);
                var eventProgress = progressByEvent.GetValueOrDefault(v1EventId);
                var notes = eventProgress?.Notes;
                events.Add(new V1LegendaryEventSource(
                    v1EventId,
                    teamsByEvent.GetValueOrDefault(key),
                    string.IsNullOrWhiteSpace(notes) ? null : notes,
                    eventProgress));
            }

            return V1LegendaryEventImportData.Valid(events, ReadShowPaidOptions(settings));
        }
        catch (JsonException)
        {
            return V1LegendaryEventImportData.Invalid();
        }
    }

    /// <summary>Progress entries by numeric V1 event key. Non-numeric keys, null entries and entries that cannot
    /// be read are dropped: progress is never a reason to fail the teams.</summary>
    private static Dictionary<int, V1LreProgress> ReadProgress(JsonElement? progress)
    {
        var byEvent = new Dictionary<int, V1LreProgress>();
        if (progress is not { ValueKind: JsonValueKind.Object } progressElement)
        {
            return byEvent;
        }

        foreach (var entry in progressElement.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var v1EventId)
                || entry.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                if (entry.Value.Deserialize<V1LreProgress>(WebJsonOptions) is { } value)
                {
                    byEvent[v1EventId] = value;
                }
            }
            catch (JsonException)
            {
                // An unreadable entry only drops this event's notes, run inputs and marks.
            }
        }

        return byEvent;
    }

    private static bool? ReadShowPaidOptions(JsonElement? settings) =>
        settings is { ValueKind: JsonValueKind.Object } element
        && element.TryGetProperty("showP2POptions", out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static List<V1Goal> ReadGoals(V1UserData? data) => data?.Goals ?? [];

    internal static V1OnslaughtImportData ReadOnslaughtProgress(V1UserData? data)
    {
        if (data?.OnslaughtPreferences is not { } preferences)
        {
            return V1OnslaughtImportData.Missing();
        }

        if (!TryReadAlliance(preferences.Imperial, out var imperial)
            || !TryReadAlliance(preferences.Xenos, out var xenos)
            || !TryReadAlliance(preferences.Chaos, out var chaos))
        {
            return V1OnslaughtImportData.Invalid();
        }

        return V1OnslaughtImportData.Valid(new V1OnslaughtProgress(imperial, xenos, chaos));
    }

    internal static V1CampaignEventProgressImportData ReadCampaignEventProgress(V1UserData? data)
    {
        if (data?.CampaignsProgress is not { } campaignsProgress)
        {
            return V1CampaignEventProgressImportData.Missing();
        }

        var result = new List<V1CampaignEventProgress>();
        foreach (var mapping in V1CampaignEventMappings)
        {
            // Matched case-insensitively — same tolerance the old JsonElement walk had for the V1
            // backend's key casing.
            var match = campaignsProgress.FirstOrDefault(kvp =>
                string.Equals(kvp.Key, mapping.V1Key, StringComparison.OrdinalIgnoreCase));
            if (match.Key is null)
            {
                continue;
            }

            var completed = match.Value;
            if (completed is < 0 or > 30)
            {
                return V1CampaignEventProgressImportData.Invalid();
            }

            result.Add(new V1CampaignEventProgress(mapping.CampaignGroupId, mapping.Type, completed));
        }

        return result.Count == 0
            ? V1CampaignEventProgressImportData.Missing()
            : V1CampaignEventProgressImportData.Valid(result);
    }

    private static bool TryReadAlliance(V1OnslaughtAllianceData? allianceData, out V1OnslaughtAllianceProgress progress)
    {
        progress = null!;
        if (allianceData is not { Sector: { } sector, Tier: { } tier })
        {
            return false;
        }

        var normalizedSector = SupportedOnslaughtSectors.FirstOrDefault(candidate =>
            string.Equals(candidate, sector, StringComparison.OrdinalIgnoreCase));
        if (normalizedSector is null || tier is < 1 or > 4)
        {
            return false;
        }

        progress = new V1OnslaughtAllianceProgress(normalizedSector, tier);
        return true;
    }

    private static readonly string[] SupportedOnslaughtSectors =
        ["Stone", "Iron", "Bronze", "Silver", "Gold", "Diamond", "Adamantine"];

    private static readonly (string V1Key, string CampaignGroupId, string Type)[] V1CampaignEventMappings =
    [
        ("Adeptus Mechanicus Standard", "eventCampaign1", "Standard"),
        ("Adeptus Mechanicus Extremis", "eventCampaign1", "Extremis"),
        ("Tyranids Standard", "eventCampaign2", "Standard"),
        ("Tyranids Extremis", "eventCampaign2", "Extremis"),
        ("T'au Empire Standard", "eventCampaign3", "Standard"),
        ("T'au Empire Extremis", "eventCampaign3", "Extremis"),
        ("Death Guard Standard", "eventCampaign4", "Standard"),
        ("Death Guard Extremis", "eventCampaign4", "Extremis"),
        ("Adepta Sororitas Standard", "eventCampaign5", "Standard"),
        ("Adepta Sororitas Extremis", "eventCampaign5", "Extremis"),
        ("Dark Angels Standard", "eventCampaign6", "Standard"),
        ("Dark Angels Extremis", "eventCampaign6", "Extremis"),
    ];

    private sealed record V1LoginRequest(string Username, string Password);

    private sealed record V1LoginResponse(string? AccessToken);

    // The V1 `GET users/me` response carries integration fields plus the planner data used for
    // selective goal and Onslaught-progress imports. Data is deserialized directly into V1UserData as
    // part of this same envelope — no separate JsonElement walk downstream.
    private sealed record V1UserDataResponse(
        string? TacticusApiKey,
        string? TacticusUserId,
        string? TacticusGuildApiKey,
        V1UserData? Data
    );
}

/// <summary>The subset of the V1 `GET users/me` response's <c>data</c> blob this importer reads. Only
/// <see cref="Goals"/>/<see cref="OnslaughtPreferences"/>/<see cref="CampaignsProgress"/> and the Legendary
/// Event blobs are modeled — V1's <c>data</c> object carries plenty else, silently ignored by
/// System.Text.Json's default unmatched-property behavior. The Legendary Event blobs stay raw (see
/// <see cref="TacticusV1Client.ReadLegendaryEvents"/>).</summary>
internal sealed record V1UserData(
    List<V1Goal>? Goals,
    V1OnslaughtPreferencesData? OnslaughtPreferences,
    Dictionary<string, int>? CampaignsProgress,
    JsonElement? LeTeams = null,
    JsonElement? LegendaryEvents3 = null,
    JsonElement? LeProgress = null,
    JsonElement? LegendaryEventsProgress = null,
    JsonElement? LeSettings = null
);

internal sealed record V1OnslaughtPreferencesData(
    V1OnslaughtAllianceData? Imperial,
    V1OnslaughtAllianceData? Xenos,
    V1OnslaughtAllianceData? Chaos
);

internal sealed record V1OnslaughtAllianceData(string? Sector, int? Tier);

public static class TacticusV1ClientRegistration
{
    // The V1 backend is an Azure Functions app whose HTTP-triggered endpoints (LoginUser, GetUserData) are
    // declared at AuthorizationLevel.Function: every request must carry a valid function key, either as the
    // x-functions-key header (used here) or a ?code= query string value.
    private const string FunctionsKeyHeaderName = "x-functions-key";

    public static IServiceCollection AddTacticusV1Client(
        this IServiceCollection services,
        string? baseUrl,
        string? functionsKey
    )
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentNullException(nameof(baseUrl), "The baseUrl cannot be null or empty.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException("The baseUrl must be an absolute URI.", nameof(baseUrl));
        }

        services.AddHttpClient(TacticusV1Client.HttpClientName, client =>
        {
            client.BaseAddress = baseUri;

            if (!string.IsNullOrWhiteSpace(functionsKey))
            {
                client.DefaultRequestHeaders.Add(FunctionsKeyHeaderName, functionsKey);
            }
        });
        services.AddScoped<ITacticusV1Client, TacticusV1Client>();

        return services;
    }
}
