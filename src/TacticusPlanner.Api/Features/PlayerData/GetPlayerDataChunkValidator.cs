using FastEndpoints;
using FluentValidation;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Validates the route-bound chunk key against the known set before the handler runs, rather than the
/// handler checking it and returning a manual <c>NotFound</c>. An unknown chunk key is a malformed
/// request, not a missing resource, so this now reports 400 (see the endpoint's updated Summary).
/// </summary>
public sealed class GetPlayerDataChunkValidator : Validator<GetPlayerDataChunkRequest>
{
    public GetPlayerDataChunkValidator()
    {
        RuleFor(request => request.Chunk)
            .Must(chunk => PlayerDataChunkKeys.All.Contains(chunk, StringComparer.Ordinal))
            .WithMessage("Unknown player-data chunk key.");
    }
}
