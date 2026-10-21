using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
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
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var profileIdValue = profileId.Value;
        var db = Resolve<PlannerDbContext>();
        var overrides = await db.PlayerDataOverrides.FirstOrDefaultAsync(entity => entity.Id == profileIdValue, ct)
            ?? new PlayerDataOverride { Id = profileIdValue };

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

        // A null alliance means "not set": no record is stored for it, so GET reports IsSet = false.
        overrides.OnslaughtProgressOverrides =
        [
            .. new[]
            {
                req.Imperial?.ToRecord("Imperial"),
                req.Xenos?.ToRecord("Xenos"),
                req.Chaos?.ToRecord("Chaos"),
            }.OfType<OnslaughtProgressOverrideRecord>(),
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

        await Send.OkAsync(OnslaughtProgressResponse.From(overrides), ct);
    }

    /// <summary>409 <c>concurrency</c> with the overrides re-read after the failed write, in the read shape.</summary>
    private async Task SendConflictAsync(PlannerDbContext db, ProfileId profileId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var current = await db.PlayerDataOverrides.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == profileId, ct)
            ?? new PlayerDataOverride { Id = profileId };
        await this.SendConflictAsync(
            ApiProblems.Concurrency, "Player data overrides changed on another device. Refresh and try again.",
            OnslaughtProgressResponse.From(current), ct);
    }
}

/// <summary>
/// One alliance's saved Onslaught position. <see cref="IsSet"/> is false when the player has never
/// saved a position for the alliance; Sector/Tier then carry the Stone 1 placeholder and must not be
/// treated as the player's real progress.
/// </summary>
public sealed record OnslaughtAllianceProgressResponse(string Sector, int Tier, bool IsSet)
{
    internal static OnslaughtAllianceProgressResponse From(
        IReadOnlyCollection<OnslaughtProgressOverrideRecord> records,
        string alliance)
    {
        var progress = records.FirstOrDefault(record =>
            string.Equals(record.Alliance, alliance, StringComparison.OrdinalIgnoreCase));
        return progress is null ? new("Stone", 1, false) : new(progress.Sector, progress.Tier, true);
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

/// <summary>
/// Replaces all three alliances' Onslaught positions. A null alliance clears it back to "not set".
/// </summary>
public sealed record UpdateOnslaughtProgressRequest(
    OnslaughtAllianceProgressRequest? Imperial,
    OnslaughtAllianceProgressRequest? Xenos,
    OnslaughtAllianceProgressRequest? Chaos,
    long Revision);

public sealed class UpdateOnslaughtProgressValidator : Validator<UpdateOnslaughtProgressRequest>
{
    private static readonly string[] SupportedSectors =
        ["Stone", "Iron", "Bronze", "Silver", "Gold", "Diamond", "Adamantine"];

    public UpdateOnslaughtProgressValidator()
    {
        RuleFor(request => request.Imperial!).SetValidator(new AllianceProgressValidator()).When(request => request.Imperial is not null);
        RuleFor(request => request.Xenos!).SetValidator(new AllianceProgressValidator()).When(request => request.Xenos is not null);
        RuleFor(request => request.Chaos!).SetValidator(new AllianceProgressValidator()).When(request => request.Chaos is not null);
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
