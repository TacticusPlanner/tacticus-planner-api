using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.PlayerDataOverrides;

public sealed class GetIncursionProgressEndpoint : EndpointWithoutRequest<IncursionProgressOverridesResponse>
{
    public override void Configure()
    {
        Get("player-data-overrides/incursions-progress");
        Summary(summary => summary.Summary = "Gets the current profile's manual Incursion progress.");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var profileIdValue = profileId.Value;
        var db = Resolve<PlannerDbContext>();
        var overrides = await db.PlayerDataOverrides.FirstOrDefaultAsync(entity => entity.Id == profileIdValue, ct)
            ?? new PlayerDataOverride { Id = profileIdValue };

        await Send.OkAsync(IncursionProgressOverridesResponse.From(overrides), ct);
    }
}

public sealed class UpdateIncursionProgressEndpoint
    : Endpoint<UpdateIncursionProgressRequest, IncursionProgressOverridesResponse>
{
    public override void Configure()
    {
        Put("player-data-overrides/incursions-progress");
        Summary(summary => summary.Summary = "Replaces the current profile's manual Incursion progress.");
    }

    public override async Task HandleAsync(UpdateIncursionProgressRequest req, CancellationToken ct)
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
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var profileIdValue = profileId.Value;
        var db = Resolve<PlannerDbContext>();
        var overrides = await db.PlayerDataOverrides.FirstOrDefaultAsync(entity => entity.Id == profileIdValue, ct);
        if (overrides is null)
        {
            if (req.Revision != 0)
            {
                await SendConflictAsync(db, profileIdValue, ct);
                return;
            }

            overrides = new PlayerDataOverride { Id = profileIdValue };
            db.PlayerDataOverrides.Add(overrides);
        }
        else if (overrides.Revision != req.Revision)
        {
            await SendConflictAsync(db, profileIdValue, ct);
            return;
        }

        // A MoW left out (or saved at 0) has no record, so the stored list holds only real progress.
        overrides.IncursionProgressOverrides =
        [
            .. req.Progress
                .Where(entry => entry.TiersCleared > 0)
                .Select(entry => new IncursionProgressOverrideRecord { MowId = entry.MowId, TiersCleared = entry.TiersCleared }),
        ];
        if (db.Entry(overrides).State != EntityState.Added)
        {
            // Replacing only a JSON-owned collection does not always mark its owner Modified. The
            // revision/updated-at interceptor operates on the owner, so make that write explicit.
            db.Entry(overrides).State = EntityState.Modified;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await SendConflictAsync(db, profileIdValue, ct);
            return;
        }

        await Send.OkAsync(IncursionProgressOverridesResponse.From(overrides), ct);
    }

    /// <summary>409 <c>concurrency</c> with the overrides re-read after the failed write, in the read shape.</summary>
    private async Task SendConflictAsync(PlannerDbContext db, ProfileId profileId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var current = await db.PlayerDataOverrides.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == profileId, ct)
            ?? new PlayerDataOverride { Id = profileId };
        await this.SendConflictAsync(
            ApiProblems.Concurrency, "Player data overrides changed on another device. Refresh and try again.",
            IncursionProgressOverridesResponse.From(current), ct);
    }

    /// <summary>Every entry names a served Incursion and clears at most its tier count.</summary>
    internal static bool ValidateAgainstCatalog(
        IReadOnlyList<IncursionProgressOverrideRequest> progress,
        GameCatalogSnapshot catalog,
        out string? error)
    {
        var tierCounts = catalog.IncursionViews.ToDictionary(
            incursion => incursion.MowId, incursion => incursion.Tiers.Count, StringComparer.Ordinal);
        foreach (var entry in progress)
        {
            if (!tierCounts.TryGetValue(entry.MowId, out var tiers))
            {
                error = $"Unknown Incursion '{entry.MowId}'.";
                return false;
            }

            if (entry.TiersCleared > tiers)
            {
                error = $"Incursion '{entry.MowId}' has {tiers} tiers; tiers cleared must be between 0 and {tiers}.";
                return false;
            }
        }

        error = null;
        return true;
    }
}

/// <summary>One MoW's cleared Incursion tiers. A MoW with no entry has cleared none.</summary>
public sealed record IncursionProgressOverrideResponse(string MowId, int TiersCleared);

public sealed record IncursionProgressOverridesResponse(
    IReadOnlyList<IncursionProgressOverrideResponse> Progress,
    long Revision)
{
    public static IncursionProgressOverridesResponse From(PlayerDataOverride overrides) => new(
        overrides.IncursionProgressOverrides
            .Select(record => new IncursionProgressOverrideResponse(record.MowId, record.TiersCleared))
            .ToArray(),
        overrides.Revision);
}

public sealed record IncursionProgressOverrideRequest(string MowId, int TiersCleared);

/// <summary>Replaces every MoW's Incursion progress; a MoW left out, or saved at 0, has cleared none.</summary>
public sealed record UpdateIncursionProgressRequest(
    IReadOnlyList<IncursionProgressOverrideRequest> Progress,
    long Revision);

public sealed class UpdateIncursionProgressValidator : Validator<UpdateIncursionProgressRequest>
{
    public UpdateIncursionProgressValidator()
    {
        RuleFor(request => request.Progress).NotNull();
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
        RuleForEach(request => request.Progress).ChildRules(entry =>
        {
            entry.RuleFor(value => value.MowId).NotEmpty();
            entry.RuleFor(value => value.TiersCleared).GreaterThanOrEqualTo(0);
        });
        RuleFor(request => request.Progress)
            .Must(progress => progress.Select(entry => entry.MowId).Distinct(StringComparer.Ordinal).Count() == progress.Count)
            .When(request => request.Progress is not null)
            .WithMessage("Each MoW may appear only once.");
    }
}
