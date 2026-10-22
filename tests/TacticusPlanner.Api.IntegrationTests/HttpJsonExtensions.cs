using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>The API's wire JSON, test-side: web defaults plus string enums (the API registers
/// <see cref="JsonStringEnumConverter"/> globally and rejects integer enums).</summary>
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Shadows the <c>System.Net.Http.Json</c> overloads the tests call so every request and response uses
/// <see cref="TestJson.Options"/>: extension methods in the tests' own namespace win over imported ones.</summary>
internal static class HttpJsonExtensions
{
    public static Task<HttpResponseMessage> PostAsJsonAsync<T>(
        this HttpClient client, string? requestUri, T value, CancellationToken cancellationToken = default) =>
        HttpClientJsonExtensions.PostAsJsonAsync(client, requestUri, value, TestJson.Options, cancellationToken);

    public static Task<HttpResponseMessage> PutAsJsonAsync<T>(
        this HttpClient client, string? requestUri, T value, CancellationToken cancellationToken = default) =>
        HttpClientJsonExtensions.PutAsJsonAsync(client, requestUri, value, TestJson.Options, cancellationToken);

    public static Task<T?> GetFromJsonAsync<T>(
        this HttpClient client, string? requestUri, CancellationToken cancellationToken = default) =>
        HttpClientJsonExtensions.GetFromJsonAsync<T>(client, requestUri, TestJson.Options, cancellationToken);

    public static Task<T?> ReadFromJsonAsync<T>(this HttpContent content, CancellationToken cancellationToken = default) =>
        HttpContentJsonExtensions.ReadFromJsonAsync<T>(content, TestJson.Options, cancellationToken);
}
