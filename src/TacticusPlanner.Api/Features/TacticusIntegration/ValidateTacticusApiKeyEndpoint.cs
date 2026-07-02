using FastEndpoints;

namespace TacticusPlanner.Api.Features.TacticusIntegration;

public sealed class ValidateTacticusApiKeyEndpoint
    : Endpoint<ValidateTacticusApiKeyRequest, ValidateTacticusApiKeyResponse>
{
    public override void Configure()
    {
        Post("tacticus-api-key/validate");
        Summary(summary =>
        {
            summary.Summary = "Validates a Tacticus API key.";
            summary.Response<ValidateTacticusApiKeyResponse>(
                StatusCodes.Status200OK,
                "The Tacticus player metadata returned by a valid API key."
            );
            summary.Response(StatusCodes.Status400BadRequest, "The Tacticus API key is missing or invalid.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is not authenticated.");
            summary.Response(StatusCodes.Status403Forbidden, "The authenticated user cannot access the API.");
        });
    }

    public override async Task HandleAsync(ValidateTacticusApiKeyRequest req, CancellationToken ct)
    {
        var tacticusApiKey = Normalize(req.TacticusApiKey);
        if (tacticusApiKey is null)
        {
            AddError(request => request.TacticusApiKey, "The Tacticus API key is required.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var validation = await Resolve<TacticusApiKeyValidator>().ValidateAsync(tacticusApiKey, ct);
        if (validation is null)
        {
            AddError(request => request.TacticusApiKey, "The Tacticus API key could not be validated.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await Send.OkAsync(new ValidateTacticusApiKeyResponse(
            validation.PlayerName,
            validation.PowerLevel
        ), ct);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record ValidateTacticusApiKeyRequest(string? TacticusApiKey);

public sealed record ValidateTacticusApiKeyResponse(string PlayerName, int PowerLevel);
