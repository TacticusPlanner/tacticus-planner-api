using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.PlayerDataOverrides;

public sealed class GetCampaignEventProgressEndpoint
    : EndpointWithoutRequest<CampaignEventProgressOverridesResponse>
{
    public override void Configure()
    {
        Get("me/player-data-overrides/campaign-events-progress");
        Summary(summary => summary.Summary = "Gets manual campaign-event progress overrides.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var profileIdValue = profileId.Value;
        var db = Resolve<PlannerDbContext>();
        var overrides = await db.PlayerDataOverrides.FirstOrDefaultAsync(entity => entity.Id == profileIdValue, ct);
        if (overrides is null)
        {
            overrides = new PlayerDataOverride { Id = profileIdValue };
            db.PlayerDataOverrides.Add(overrides);
            await db.SaveChangesAsync(ct);
        }

        await Send.OkAsync(CampaignEventProgressOverridesResponse.From(overrides), ct);
    }
}

public sealed class UpdateCampaignEventProgressEndpoint
    : Endpoint<UpdateCampaignEventProgressRequest, CampaignEventProgressOverridesResponse>
{
    public override void Configure()
    {
        Put("me/player-data-overrides/campaign-events-progress");
        Summary(summary => summary.Summary = "Replaces manual campaign-event progress overrides.");
    }

    public override async Task HandleAsync(UpdateCampaignEventProgressRequest req, CancellationToken ct)
    {
        if (!ValidateAgainstCatalog(req.Progress, Resolve<IGameCatalogProvider>().Current, out var error))
        {
            AddError(error!);
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var profileIdValue = profileId.Value;
        var db = Resolve<PlannerDbContext>();
        var overrides = await db.PlayerDataOverrides.FirstOrDefaultAsync(entity => entity.Id == profileIdValue, ct);
        if (overrides is null)
        {
            if (req.Revision != 0)
            {
                await SendConflictAsync(ct);
                return;
            }

            overrides = new PlayerDataOverride { Id = profileIdValue };
            db.PlayerDataOverrides.Add(overrides);
        }
        else if (overrides.Revision != req.Revision)
        {
            await SendConflictAsync(ct);
            return;
        }

        overrides.CampaignEventProgressOverrides = req.Progress.Select(ToRecord).ToList();
        if (db.Entry(overrides).State != EntityState.Added)
        {
            db.Entry(overrides).State = EntityState.Modified;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await SendConflictAsync(ct);
            return;
        }

        await Send.OkAsync(CampaignEventProgressOverridesResponse.From(overrides), ct);
    }

    private async Task SendConflictAsync(CancellationToken ct)
    {
        AddError("Player data overrides changed on another device. Refresh and try again.");
        await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
    }

    internal static bool ValidateAgainstCatalog(
        IReadOnlyList<CampaignEventProgressOverrideRequest> progress,
        GameCatalogSnapshot catalog,
        out string? error)
    {
        foreach (var entry in progress)
        {
            var group = catalog.CampaignGroups.Values.FirstOrDefault(value =>
                string.Equals(value.GroupId, entry.CampaignGroupId, StringComparison.Ordinal));
            if (group is null || !string.Equals(group.ReleaseType, "event", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Unknown campaign-event group '{entry.CampaignGroupId}'.";
                return false;
            }

            var typeBattles = group.Battles
                .Where(battle => string.Equals(battle.Type, entry.Type, StringComparison.Ordinal))
                .ToArray();
            if (typeBattles.Length == 0)
            {
                error = $"Type '{entry.Type}' does not belong to '{entry.CampaignGroupId}'.";
                return false;
            }

            var regularMaximum = typeBattles.Count(battle => !battle.Challenge);
            if (entry.CompletedBattleCount is < 0 || entry.CompletedBattleCount > regularMaximum)
            {
                error = $"Completed battle count must be between 0 and {regularMaximum}.";
                return false;
            }

            if (entry.CompletedChallengeBattlesIds is not null)
            {
                var challengeIds = new HashSet<string>(
                    typeBattles.Where(battle => battle.Challenge).Select(battle => battle.Id),
                    StringComparer.Ordinal);
                if (entry.CompletedChallengeBattlesIds.Any(id => !challengeIds.Contains(id)))
                {
                    error = "Every completed challenge battle id must belong to the selected event and type.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    private static CampaignEventProgressOverrideRecord ToRecord(CampaignEventProgressOverrideRequest request) => new()
    {
        CampaignGroupId = CampaignId.From(request.CampaignGroupId),
        Type = request.Type,
        CompletedBattleCount = request.CompletedBattleCount,
        CompletedChallengeBattlesIds = request.CompletedChallengeBattlesIds?.ToList(),
    };
}

public sealed record CampaignEventProgressOverrideResponse(
    string CampaignGroupId,
    string Type,
    int? CompletedBattleCount,
    IReadOnlyList<string>? CompletedChallengeBattlesIds
)
{
    internal static CampaignEventProgressOverrideResponse From(CampaignEventProgressOverrideRecord record) => new(
        record.CampaignGroupId.Value,
        record.Type,
        record.CompletedBattleCount,
        record.CompletedChallengeBattlesIds?.ToArray());
}

public sealed record CampaignEventProgressOverridesResponse(
    IReadOnlyList<CampaignEventProgressOverrideResponse> Progress,
    long Revision)
{
    public static CampaignEventProgressOverridesResponse From(PlayerDataOverride overrides) => new(
        overrides.CampaignEventProgressOverrides.Select(CampaignEventProgressOverrideResponse.From).ToArray(),
        overrides.Revision);
}

public sealed record CampaignEventProgressOverrideRequest(
    string CampaignGroupId,
    string Type,
    int? CompletedBattleCount,
    IReadOnlyList<string>? CompletedChallengeBattlesIds);

public sealed record UpdateCampaignEventProgressRequest(
    IReadOnlyList<CampaignEventProgressOverrideRequest> Progress,
    long Revision);

public sealed class UpdateCampaignEventProgressValidator : Validator<UpdateCampaignEventProgressRequest>
{
    public UpdateCampaignEventProgressValidator()
    {
        RuleFor(request => request.Progress).NotNull();
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
        RuleForEach(request => request.Progress).ChildRules(entry =>
        {
            entry.RuleFor(value => value.CampaignGroupId).NotEmpty();
            entry.RuleFor(value => value.Type).NotEmpty();
            entry.RuleFor(value => value)
                .Must(value => value.CompletedBattleCount is not null
                    || value.CompletedChallengeBattlesIds is not null)
                .WithMessage("At least one manual event-progress value is required.");
            entry.RuleFor(value => value.CompletedChallengeBattlesIds)
                .Must(ids => ids is null || ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
                .WithMessage("Completed challenge battle ids must be unique.");
        });
        RuleFor(request => request.Progress)
            .Must(progress => progress
                .Select(entry => (entry.CampaignGroupId, entry.Type))
                .Distinct()
                .Count() == progress.Count)
            .WithMessage("Campaign event and type entries must be unique.");
    }
}
