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

public sealed record V1Goal(
    string? Id,
    string? Character,
    int Type,
    int Priority,
    bool DailyRaids,
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
    int? SecondAbilityLevel
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

        var payload = await response.Content.ReadFromJsonAsync<V1UserDataResponse>(cancellationToken);

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
        );
    }

    private static List<V1Goal> ReadGoals(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } value
            || !value.TryGetProperty("goals", out var goals)
            || goals.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return goals.Deserialize<List<V1Goal>>(WebJsonOptions) ?? [];
    }

    internal static V1OnslaughtImportData ReadOnslaughtProgress(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } value
            || !TryGetProperty(value, "onslaughtPreferences", out var preferences))
        {
            return V1OnslaughtImportData.Missing();
        }

        if (preferences.ValueKind != JsonValueKind.Object
            || !TryReadAlliance(preferences, "Imperial", out var imperial)
            || !TryReadAlliance(preferences, "Xenos", out var xenos)
            || !TryReadAlliance(preferences, "Chaos", out var chaos))
        {
            return V1OnslaughtImportData.Invalid();
        }

        return V1OnslaughtImportData.Valid(new V1OnslaughtProgress(imperial, xenos, chaos));
    }

    internal static V1CampaignEventProgressImportData ReadCampaignEventProgress(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } value
            || !TryGetProperty(value, "campaignsProgress", out var campaignsProgress))
        {
            return V1CampaignEventProgressImportData.Missing();
        }

        if (campaignsProgress.ValueKind != JsonValueKind.Object)
        {
            return V1CampaignEventProgressImportData.Invalid();
        }

        var result = new List<V1CampaignEventProgress>();
        foreach (var mapping in V1CampaignEventMappings)
        {
            if (!TryGetProperty(campaignsProgress, mapping.V1Key, out var progress))
            {
                continue;
            }

            if (!progress.TryGetInt32(out var completed) || completed is < 0 or > 30)
            {
                return V1CampaignEventProgressImportData.Invalid();
            }

            result.Add(new V1CampaignEventProgress(mapping.CampaignGroupId, mapping.Type, completed));
        }

        return result.Count == 0
            ? V1CampaignEventProgressImportData.Missing()
            : V1CampaignEventProgressImportData.Valid(result);
    }

    private static bool TryReadAlliance(
        JsonElement preferences,
        string alliance,
        out V1OnslaughtAllianceProgress progress)
    {
        progress = null!;
        if (!TryGetProperty(preferences, alliance, out var value)
            || value.ValueKind != JsonValueKind.Object
            || !TryGetProperty(value, "sector", out var sectorElement)
            || sectorElement.ValueKind != JsonValueKind.String
            || !TryGetProperty(value, "tier", out var tierElement)
            || !tierElement.TryGetInt32(out var tier))
        {
            return false;
        }

        var sector = sectorElement.GetString();
        var normalizedSector = SupportedOnslaughtSectors.FirstOrDefault(candidate =>
            string.Equals(candidate, sector, StringComparison.OrdinalIgnoreCase));
        if (normalizedSector is null || tier is < 1 or > 4)
        {
            return false;
        }

        progress = new V1OnslaughtAllianceProgress(normalizedSector, tier);
        return true;
    }

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement property)
    {
        foreach (var candidate in value.EnumerateObject().Where(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            property = candidate.Value;
            return true;
        }

        property = default;
        return false;
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
    // selective goal and Onslaught-progress imports.
    private sealed record V1UserDataResponse(
        string? TacticusApiKey,
        string? TacticusUserId,
        string? TacticusGuildApiKey,
        JsonElement? Data
    );
}

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
