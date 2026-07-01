using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Refit;

namespace TacticusPlanner.TacticusApi;

public static class DependencyInjection
{
    public static IServiceCollection AddTacticusApi(this IServiceCollection services, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentNullException(nameof(baseUrl), "The baseUrl cannot be null or empty.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new ArgumentException("The baseUrl must be an absolute URI.", nameof(baseUrl));
        }

        // The Tacticus API serializes enums (rarity, encounterType, damageType) as strings,
        // which System.Text.Json does not bind by default. Add a string-enum converter on top
        // of Refit's Web defaults (camelCase, case-insensitive) so typed models populate correctly.
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var refitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(jsonOptions)
        };

        services.AddRefitClient<ITacticusApi>(refitSettings)
            .ConfigureHttpClient(c => c.BaseAddress = baseUri);

        return services;
    }
}
