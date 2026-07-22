using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.PlayerDataOverrides;

public sealed class GetOnslaughtProgressEndpoint : EndpointWithoutRequest<OnslaughtProgressResponse>
{
    public override void Configure()
    {
        Get("me/player-data-overrides/onslaught-progress");
        Summary(summary => summary.Summary = "Gets the current profile's manual Onslaught progress.");
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

        await Send.OkAsync(OnslaughtProgressResponse.From(overrides), ct);
    }
}

public sealed class UpdateOnslaughtProgressEndpoint
    : Endpoint<UpdateOnslaughtProgressRequest, OnslaughtProgressResponse>
{
    public override void Configure()
    {
        Put("me/player-data-overrides/onslaught-progress");
        Summary(summary => summary.Summary = "Updates the current profile's manual Onslaught progress.");
    }

    public override async Task HandleAsync(UpdateOnslaughtProgressRequest req, CancellationToken ct)
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
            if (req.Revision != 0)
            {
                AddError("Player data overrides changed on another device. Refresh and try again.");
                await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
                return;
            }

            overrides = new PlayerDataOverride { Id = profileIdValue };
            db.PlayerDataOverrides.Add(overrides);
        }
        else if (overrides.Revision != req.Revision)
        {
            AddError("Player data overrides changed on another device. Refresh and try again.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        overrides.OnslaughtProgressOverrides =
        [
            req.Imperial.ToRecord("Imperial"),
            req.Xenos.ToRecord("Xenos"),
            req.Chaos.ToRecord("Chaos"),
        ];
        // Replacing only a JSON-owned collection does not always mark its owner Modified. The
        // revision/updated-at interceptor operates on the owner, so make that write explicit.
        db.Entry(overrides).State = EntityState.Modified;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            AddError("Player data overrides changed on another device. Refresh and try again.");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await Send.OkAsync(OnslaughtProgressResponse.From(overrides), ct);
    }
}

public sealed record OnslaughtAllianceProgressResponse(string Sector, int Tier)
{
    internal static OnslaughtAllianceProgressResponse From(
        IReadOnlyCollection<OnslaughtProgressOverrideRecord> records,
        string alliance)
    {
        var progress = records.FirstOrDefault(record =>
            string.Equals(record.Alliance, alliance, StringComparison.OrdinalIgnoreCase));
        return progress is null ? new("Stone", 1) : new(progress.Sector, progress.Tier);
    }
}

public sealed record OnslaughtProgressResponse(
    OnslaughtAllianceProgressResponse Imperial,
    OnslaughtAllianceProgressResponse Xenos,
    OnslaughtAllianceProgressResponse Chaos,
    long Revision)
{
    public static OnslaughtProgressResponse From(PlayerDataOverride overrides) => new(
        OnslaughtAllianceProgressResponse.From(overrides.OnslaughtProgressOverrides, "Imperial"),
        OnslaughtAllianceProgressResponse.From(overrides.OnslaughtProgressOverrides, "Xenos"),
        OnslaughtAllianceProgressResponse.From(overrides.OnslaughtProgressOverrides, "Chaos"),
        overrides.Revision);
}

public sealed record OnslaughtAllianceProgressRequest(string Sector, int Tier)
{
    internal OnslaughtProgressOverrideRecord ToRecord(string alliance) => new()
    {
        Alliance = alliance,
        Sector = Sector,
        Tier = Tier,
    };
}

public sealed record UpdateOnslaughtProgressRequest(
    OnslaughtAllianceProgressRequest Imperial,
    OnslaughtAllianceProgressRequest Xenos,
    OnslaughtAllianceProgressRequest Chaos,
    long Revision);

public sealed class UpdateOnslaughtProgressValidator : Validator<UpdateOnslaughtProgressRequest>
{
    private static readonly string[] SupportedSectors =
        ["Stone", "Iron", "Bronze", "Silver", "Gold", "Diamond", "Adamantine"];

    public UpdateOnslaughtProgressValidator()
    {
        RuleFor(request => request.Imperial).SetValidator(new AllianceProgressValidator());
        RuleFor(request => request.Xenos).SetValidator(new AllianceProgressValidator());
        RuleFor(request => request.Chaos).SetValidator(new AllianceProgressValidator());
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
    }

    private sealed class AllianceProgressValidator : Validator<OnslaughtAllianceProgressRequest>
    {
        public AllianceProgressValidator()
        {
            RuleFor(request => request.Sector).Must(sector => SupportedSectors.Contains(sector, StringComparer.OrdinalIgnoreCase));
            RuleFor(request => request.Tier).InclusiveBetween(1, 4);
        }
    }
}
