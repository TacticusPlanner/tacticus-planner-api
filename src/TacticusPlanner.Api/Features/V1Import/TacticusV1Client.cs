using System.Net.Http.Headers;

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

public sealed record TacticusV1Profile(string? TacticusApiKey, string? TacticusUserId);

public sealed class TacticusV1Client(IHttpClientFactory httpClientFactory) : ITacticusV1Client
{
    public const string HttpClientName = "TacticusV1";

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
            string.IsNullOrWhiteSpace(payload.TacticusUserId) ? null : payload.TacticusUserId
        );
    }

    private sealed record V1LoginRequest(string Username, string Password);

    private sealed record V1LoginResponse(string? AccessToken);

    // The V1 `GET users/me` response carries many legacy planner fields; only these two are relevant to import.
    private sealed record V1UserDataResponse(string? TacticusApiKey, string? TacticusUserId);
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
