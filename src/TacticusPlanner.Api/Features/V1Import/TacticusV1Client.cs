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
    V1CampaignEventProgressImportData CampaignEventProgress,
    // Optional so the older fixtures that build a profile positionally keep compiling; a null reads as
    // "no LRE keys in the V1 blob" (see LegendaryEventsOrMissing).
    V1LegendaryEventImportData? LegendaryEvents = null
)
{
    public TacticusV1Profile(string? tacticusApiKey, string? tacticusUserId)
        : this(tacticusApiKey, tacticusUserId, null, [], V1OnslaughtImportData.Missing(),
            V1CampaignEventProgressImportData.Missing())
    {
    }

    public V1LegendaryEventImportData LegendaryEventsOrMissing => LegendaryEvents ?? V1LegendaryEventImportData.Missing();
}

/// <summary>One V1 event's teams and notes, keyed by V1's numeric <c>LegendaryEventEnum</c>. The legacy
/// <c>alpha</c>/<c>beta</c>/<c>gamma</c> maps (restriction name → unit ids) are only read when
/// <see cref="Teams"/> is empty, as V1's own loader does.</summary>
public sealed record V1LegendaryEventSource(
    int V1EventId,
    IReadOnlyList<V1LreTeam> Teams,
    IReadOnlyDictionary<string, IReadOnlyList<string>> LegacyAlpha,
    IReadOnlyDictionary<string, IReadOnlyList<string>> LegacyBeta,
    IReadOnlyDictionary<string, IReadOnlyList<string>> LegacyGamma,
    string? Notes
);

/// <summary>A V1 <c>ILreTeam</c>. Unit ids may be snowprint ids (<see cref="CharSnowprintIds"/>), legacy
/// character names (<see cref="CharactersIds"/>) or embedded objects (<see cref="Characters"/>); objectives
/// (<see cref="RestrictionsIds"/>) are display names, not indexes.</summary>
public sealed record V1LreTeam(
    string? Id,
    string? Name,
    string? Section,
    List<string>? RestrictionsIds,
    List<string>? CharSnowprintIds,
    List<string>? CharactersIds,
    List<V1LreTeamCharacter>? Characters,
    int? ExpectedBattleClears
);

public sealed record V1LreTeamCharacter(string? SnowprintId, string? Name);

/// <summary>Distinguishes a profile with no V1 LRE team keys from one whose keys exist but cannot be read.
/// The import endpoint reports those cases as Skipped and Failed respectively.</summary>
public sealed record V1LegendaryEventImportData(bool IsPresent, IReadOnlyList<V1LegendaryEventSource>? Events)
{
    public static V1LegendaryEventImportData Missing() => new(false, null);

    public static V1LegendaryEventImportData Invalid() => new(true, null);

    public static V1LegendaryEventImportData Valid(IReadOnlyList<V1LegendaryEventSource> events) => new(true, events);
}

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
    int? MythicCampaignsUsage = null
);

public sealed class TacticusV1Client(IHttpClientFactory httpClientFactory) : ITacticusV1Client
{
    public const string HttpClientName = "TacticusV1";
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

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
            ReadCampaignEventProgress(payload.Data),
            ReadLegendaryEvents(payload.Data)
        );
    }

    /// <summary>Reads <c>leTeams</c> (falling back to the pre-V2-schema <c>legendaryEvents3</c>) and the
    /// per-event notes from <c>leProgress</c> (falling back to <c>legendaryEventsProgress</c>). The blobs are
    /// kept as raw JSON on <see cref="V1UserData"/> so a malformed one is reported as Invalid for this part
    /// alone instead of failing the whole profile read.</summary>
    internal static V1LegendaryEventImportData ReadLegendaryEvents(V1UserData? data)
    {
        var teamsElement = PresentObject(data?.LeTeams) ?? PresentObject(data?.LegendaryEvents3);
        if (teamsElement is null)
        {
            return V1LegendaryEventImportData.Missing();
        }

        try
        {
            var teamsByEvent = teamsElement.Value.Deserialize<Dictionary<string, V1LegendaryEventTeamsData?>>(WebJsonOptions) ?? [];
            var notesByEvent = ReadNotes(PresentObject(data?.LeProgress) ?? PresentObject(data?.LegendaryEventsProgress));
            var events = new List<V1LegendaryEventSource>();
            foreach (var (key, value) in teamsByEvent)
            {
                if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v1EventId))
                {
                    continue;
                }

                events.Add(new V1LegendaryEventSource(
                    v1EventId,
                    value?.Teams ?? [],
                    LegacyMap(value?.Alpha),
                    LegacyMap(value?.Beta),
                    LegacyMap(value?.Gamma),
                    notesByEvent.GetValueOrDefault(v1EventId)));
            }

            return V1LegendaryEventImportData.Valid(events);
        }
        catch (JsonException)
        {
            return V1LegendaryEventImportData.Invalid();
        }
    }

    private static JsonElement? PresentObject(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Object } value ? value : null;

    private static Dictionary<int, string?> ReadNotes(JsonElement? progressElement)
    {
        var notes = new Dictionary<int, string?>();
        if (progressElement is null)
        {
            return notes;
        }

        var progressByEvent = progressElement.Value.Deserialize<Dictionary<string, V1LreProgressData?>>(WebJsonOptions) ?? [];
        foreach (var (key, value) in progressByEvent)
        {
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v1EventId))
            {
                notes[v1EventId] = value?.Notes;
            }
        }

        return notes;
    }

    private static Dictionary<string, IReadOnlyList<string>> LegacyMap(Dictionary<string, List<string>?>? map) =>
        (map ?? []).ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)(pair.Value ?? []), StringComparer.Ordinal);

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
/// <see cref="Goals"/>/<see cref="OnslaughtPreferences"/>/<see cref="CampaignsProgress"/> are modeled —
/// V1's <c>data</c> object carries plenty else, silently ignored by System.Text.Json's default
/// unmatched-property behavior.</summary>
internal sealed record V1UserData(
    List<V1Goal>? Goals,
    V1OnslaughtPreferencesData? OnslaughtPreferences,
    Dictionary<string, int>? CampaignsProgress,
    // The LRE blobs stay raw here and are parsed by TacticusV1Client.ReadLegendaryEvents, so a malformed
    // one fails only the legendaryEventPlans part. Older accounts may carry the pre-V2-schema keys
    // (legendaryEvents3 / legendaryEventsProgress) instead; both spellings are read.
    JsonElement? LeTeams = null,
    JsonElement? LegendaryEvents3 = null,
    JsonElement? LeProgress = null,
    JsonElement? LegendaryEventsProgress = null
);

internal sealed record V1LegendaryEventTeamsData(
    List<V1LreTeam>? Teams,
    Dictionary<string, List<string>?>? Alpha,
    Dictionary<string, List<string>?>? Beta,
    Dictionary<string, List<string>?>? Gamma
);

internal sealed record V1LreProgressData(string? Notes);

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
