using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        Summary(summary => summary.Summary = "Gets the current profile's manual Incursion battle progress.");
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
        Summary(summary => summary.Summary = "Replaces the current profile's manual Incursion battle progress.");
    }

    public override async Task HandleAsync(UpdateIncursionProgressRequest req, CancellationToken ct)
    {
        if (!ValidateAgainstCatalog(req.Battles, Resolve<IGameCatalogProvider>().Current, out var error))
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

        // A battle left out (or saved at 0 stars) has no record, so the stored list holds only real progress.
        overrides.IncursionBattleProgressOverrides =
        [
            .. req.Battles
                .Where(entry => entry.Stars > 0)
                .Select(entry => new IncursionBattleProgressOverrideRecord
                {
                    MowId = entry.MowId,
                    TierIndex = entry.TierIndex,
                    BattleId = entry.BattleId,
                    Stars = entry.Stars,
                }),
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
        catch (DbUpdateException exception) when (IsFirstWriteCollision(exception))
        {
            // Another device created this profile's overrides row first; same outcome as a stale revision.
            await SendConflictAsync(db, profileIdValue, ct);
            return;
        }

        await Send.OkAsync(IncursionProgressOverridesResponse.From(overrides), ct);
    }

    private static bool IsFirstWriteCollision(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            TableName: "player_data_overrides",
        };

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

    /// <summary>Every entry names a battle of a served Incursion tier.</summary>
    internal static bool ValidateAgainstCatalog(
        IReadOnlyList<IncursionBattleProgressOverrideRequest> battles,
        GameCatalogSnapshot catalog,
        out string? error)
    {
        var incursions = catalog.IncursionViews.ToDictionary(incursion => incursion.MowId, StringComparer.Ordinal);
        foreach (var entry in battles)
        {
            if (!incursions.TryGetValue(entry.MowId, out var incursion))
            {
                error = $"Unknown Incursion '{entry.MowId}'.";
                return false;
            }

            var tier = incursion.Tiers.FirstOrDefault(candidate => candidate.Index == entry.TierIndex);
            if (tier is null)
            {
                error = $"Incursion '{entry.MowId}' has no tier {entry.TierIndex}.";
                return false;
            }

            if (!tier.Battles.Any(battle => string.Equals(battle.Id, entry.BattleId, StringComparison.Ordinal)))
            {
                error = $"Incursion '{entry.MowId}' tier {entry.TierIndex} has no battle '{entry.BattleId}'.";
                return false;
            }
        }

        error = null;
        return true;
    }
}

/// <summary>The stars earned on one Incursion battle. A battle with no entry has none.</summary>
public sealed record IncursionBattleProgressOverrideResponse(string MowId, int TierIndex, string BattleId, int Stars);

public sealed record IncursionProgressOverridesResponse(
    IReadOnlyList<IncursionBattleProgressOverrideResponse> Battles,
    long Revision)
{
    // The column was added after other overrides existed, so a row saved before it reads as null.
    public static IncursionProgressOverridesResponse From(PlayerDataOverride overrides) => new(
        (overrides.IncursionBattleProgressOverrides ?? [])
            .Select(record => new IncursionBattleProgressOverrideResponse(
                record.MowId, record.TierIndex, record.BattleId, record.Stars))
            .ToArray(),
        overrides.Revision);
}

public sealed record IncursionBattleProgressOverrideRequest(string MowId, int TierIndex, string BattleId, int Stars);

/// <summary>Replaces every Incursion battle's stars; a battle left out, or saved at 0, has none.</summary>
public sealed record UpdateIncursionProgressRequest(
    IReadOnlyList<IncursionBattleProgressOverrideRequest> Battles,
    long Revision);

public sealed class UpdateIncursionProgressValidator : Validator<UpdateIncursionProgressRequest>
{
    public UpdateIncursionProgressValidator()
    {
        RuleFor(request => request.Battles).NotNull();
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
        RuleForEach(request => request.Battles).NotNull().ChildRules(entry =>
        {
            entry.RuleFor(value => value.MowId).NotEmpty();
            entry.RuleFor(value => value.TierIndex).GreaterThanOrEqualTo(0);
            entry.RuleFor(value => value.BattleId).NotEmpty();
            entry.RuleFor(value => value.Stars).InclusiveBetween(0, 3);
        });
        RuleFor(request => request.Battles)
            .Must(battles => battles.Where(entry => entry is not null)
                .Select(entry => (entry.MowId, entry.TierIndex, entry.BattleId))
                .Distinct().Count() == battles.Count(entry => entry is not null))
            .When(request => request.Battles is not null)
            .WithMessage("Each Incursion battle may appear only once.");
    }
}
