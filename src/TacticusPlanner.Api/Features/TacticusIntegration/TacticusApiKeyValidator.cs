using Refit;
using TacticusPlanner.TacticusApi;

namespace TacticusPlanner.Api.Features.TacticusIntegration;

public sealed class TacticusApiKeyValidator(ITacticusApi tacticusApi)
{
    public async Task<TacticusApiKeyValidationResult?> ValidateAsync(
        string tacticusApiKey,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var response = await tacticusApi.GetPlayerAsync(tacticusApiKey, cancellationToken);
            var details = response.Player?.Details;

            if (details is null || string.IsNullOrWhiteSpace(details.Name))
            {
                return null;
            }

            var lastUpdatedOn = response.Metadata?.LastUpdatedOn ?? 0;

            return new TacticusApiKeyValidationResult(
                details.Name,
                details.PowerLevel,
                lastUpdatedOn > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastUpdatedOn) : null
            );
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404)
        {
            return null;
        }
    }
}

public sealed record TacticusApiKeyValidationResult(
    string PlayerName,
    int PowerLevel,
    DateTimeOffset? SourceUpdatedAt
);
