using FastEndpoints;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.FeatureFlags;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.GameCatalog;
using TacticusPlanner.GameCatalog.Models;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.PlayerDataOverrides;

public sealed class GetBattleProgressEndpoint : EndpointWithoutRequest<BattleProgressOverridesResponse>
{
    public override void Configure()
    {
        Get("player-data-overrides/battle-progress");
        this.RequireFeature(FeatureFlags.FeatureFlags.ManualBattles);
        Summary(summary => summary.Summary = "Gets manual per-battle progress overrides (medals, Lightning Victory, milestones).");
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

        await Send.OkAsync(BattleProgressOverridesResponse.From(overrides), ct);
    }
}

public sealed class UpdateBattleProgressEndpoint
    : Endpoint<UpdateBattleProgressRequest, BattleProgressOverridesResponse>
{
    public override void Configure()
    {
        Put("player-data-overrides/battle-progress");
        this.RequireFeature(FeatureFlags.FeatureFlags.ManualBattles);
        Summary(summary => summary.Summary = "Replaces manual per-battle progress overrides.");
    }

    public override async Task HandleAsync(UpdateBattleProgressRequest req, CancellationToken ct)
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

        overrides.BattleProgressOverrides = req.Battles.Select(ToRecord).ToList();
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
            await SendConflictAsync(db, profileIdValue, ct);
            return;
        }

        await Send.OkAsync(BattleProgressOverridesResponse.From(overrides), ct);
    }

    /// <summary>409 <c>concurrency</c> with the overrides re-read after the failed write, in the read shape.</summary>
    private async Task SendConflictAsync(PlannerDbContext db, ProfileId profileId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var current = await db.PlayerDataOverrides.AsNoTracking().FirstOrDefaultAsync(entity => entity.Id == profileId, ct)
            ?? new PlayerDataOverride { Id = profileId };
        await this.SendConflictAsync(
            ApiProblems.Concurrency, "Player data overrides changed on another device. Refresh and try again.",
            BattleProgressOverridesResponse.From(current), ct);
    }

    /// <summary>
    /// Every battle id must exist in the catalog; a Lightning Victory needs a battle that has one; a
    /// milestone belongs to campaign-event battles only.
    /// </summary>
    public static bool ValidateAgainstCatalog(
        IReadOnlyList<BattleProgressOverrideRequest> battles,
        GameCatalogSnapshot catalog,
        out string? error)
    {
        var groupsByBattleId = catalog.CampaignGroups.Values
            .SelectMany(group => group.Battles.Select(battle => (battle, group)))
            .ToDictionary(pair => pair.battle.Id, StringComparer.Ordinal);
        foreach (var entry in battles)
        {
            if (!groupsByBattleId.TryGetValue(entry.BattleId, out var match))
            {
                error = $"Unknown campaign battle '{entry.BattleId}'.";
                return false;
            }

            if (entry.LightningVictory == true && match.battle.LightningVictoryTurns is null)
            {
                error = $"Battle '{entry.BattleId}' has no Lightning Victory condition.";
                return false;
            }

            if (entry.Milestone is not null
                && !string.Equals(match.group.ReleaseType, "event", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Milestones apply to campaign-event battles only, not '{entry.BattleId}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static BattleProgressOverrideRecord ToRecord(BattleProgressOverrideRequest request) => new()
    {
        BattleId = request.BattleId,
        Medals = request.Medals,
        LightningVictory = request.LightningVictory,
        Milestone = request.Milestone?.ToString(),
    };
}

/// <summary>Campaign-event milestone tiers: Bronze (≤2 borrowed), Silver (≤1 borrowed), Gold (Lightning
/// Victory with no borrowed characters and none defeated).</summary>
public enum BattleMilestone
{
    Bronze,
    Silver,
    Gold,
}

public sealed record BattleProgressOverrideResponse(
    string BattleId,
    int Medals,
    bool? LightningVictory,
    BattleMilestone? Milestone)
{
    internal static BattleProgressOverrideResponse From(BattleProgressOverrideRecord record) => new(
        record.BattleId,
        record.Medals,
        record.LightningVictory,
        Enum.TryParse<BattleMilestone>(record.Milestone, out var milestone) ? milestone : null);
}

public sealed record BattleProgressOverridesResponse(
    IReadOnlyList<BattleProgressOverrideResponse> Battles,
    long Revision)
{
    // The column was added after other overrides existed, so a row saved before it reads as null.
    public static BattleProgressOverridesResponse From(PlayerDataOverride overrides) => new(
        (overrides.BattleProgressOverrides ?? []).Select(BattleProgressOverrideResponse.From).ToArray(),
        overrides.Revision);
}

public sealed record BattleProgressOverrideRequest(
    string BattleId,
    int Medals,
    bool? LightningVictory,
    BattleMilestone? Milestone);

public sealed record UpdateBattleProgressRequest(
    IReadOnlyList<BattleProgressOverrideRequest> Battles,
    long Revision);

public sealed class UpdateBattleProgressValidator : Validator<UpdateBattleProgressRequest>
{
    public UpdateBattleProgressValidator()
    {
        RuleFor(request => request.Battles).NotNull();
        RuleFor(request => request.Revision).GreaterThanOrEqualTo(0);
        RuleForEach(request => request.Battles).NotNull().ChildRules(entry =>
        {
            entry.RuleFor(value => value.BattleId).NotEmpty();
            entry.RuleFor(value => value.Medals).InclusiveBetween(0, 3);
            entry.RuleFor(value => value.Milestone).IsInEnum().When(value => value.Milestone is not null);
        });
        RuleFor(request => request.Battles)
            .Must(battles => battles.Where(entry => entry is not null).Select(entry => entry.BattleId)
                .Distinct(StringComparer.Ordinal).Count() == battles.Count(entry => entry is not null))
            .When(request => request.Battles is not null)
            .WithMessage("Battle ids must be unique.");
    }
}
