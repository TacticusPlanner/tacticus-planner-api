using FastEndpoints;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.LegendaryEventPlans;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.GuildWars;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.GuildWars;

/// <summary>Responses shared by the war endpoints.</summary>
internal static class GuildWarProblems
{
    public static GuildWarId? RouteWarId(IEndpoint endpoint) =>
        Guid.TryParse(endpoint.HttpContext.GetRouteValue("warId")?.ToString(), out var guid) ? GuildWarId.From(guid) : null;

    public static GuildWarAttackId? RouteAttackId(IEndpoint endpoint) =>
        Guid.TryParse(endpoint.HttpContext.GetRouteValue("attackId")?.ToString(), out var guid)
            ? GuildWarAttackId.From(guid)
            : null;

    /// <summary>400 <c>guildWarUnknownSeason</c> or <c>guildWarUnknownLevel</c>.</summary>
    public static Task SendSettingsProblemAsync(IEndpoint endpoint, string issueCode, CancellationToken ct)
    {
        var (field, message) = issueCode == GuildWarRules.UnknownSeason
            ? ("seasonId", "seasonId is not a served Guild War season.")
            : ("battlefieldLevel", "battlefieldLevel is not one of the season's battlefield levels.");
        return SendBadRequestAsync(endpoint, issueCode, field, message, ct);
    }

    /// <summary>400 naming the first attack rule the attack breaks.</summary>
    public static Task SendRuleBrokenAsync(IEndpoint endpoint, GuildWarAttackRuleCode code, CancellationToken ct)
    {
        var (field, message) = code switch
        {
            GuildWarAttackRuleCode.UnknownZone => ("zoneId", "The zone is not a zone of the war's season, or the slot is not 0 to 4."),
            GuildWarAttackRuleCode.UnknownUnit => ("characters", "A unit, buff or board is not known, or a unit is listed twice."),
            GuildWarAttackRuleCode.UnitDeployed => ("characters", "A lineup unit already attacked in this war."),
            _ => ("general", "Every attack token of this war is spent."),
        };
        return SendBadRequestAsync(endpoint, GuildWarsService.IssueCode(code), field, message, ct);
    }

    private static Task SendBadRequestAsync(
        IEndpoint endpoint, string issueCode, string field, string message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ApiProblems.WriteAsync(
            endpoint.HttpContext, StatusCodes.Status400BadRequest, issueCode, message,
            extensions: new Dictionary<string, object?>
            {
                ["errors"] = new Dictionary<string, string[]> { [field] = [message] },
            });
    }

    public static Task SendNotStartedAsync(IEndpoint endpoint, CancellationToken ct) =>
        endpoint.SendConflictAsync(
            GuildWarsService.NotStarted, "There is no current war. Start one first.", new CurrentGuildWarResponse(null), ct);
}

public sealed class GetCurrentGuildWarEndpoint : EndpointWithoutRequest<CurrentGuildWarResponse>
{
    public override void Configure()
    {
        Get("guild-wars/current");
        Summary(summary =>
        {
            summary.Summary = "Reads the caller's current Guild War with its attacks.";
            summary.Description = "war is null when the caller has never started a war.";
            summary.Response<CurrentGuildWarResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var war = await Resolve<GuildWarsService>().CurrentAsync(tracking: false, ct);
        await Send.OkAsync(new CurrentGuildWarResponse(war is null ? null : GuildWarResponse.From(war)), ct);
    }
}

public sealed class SetCurrentGuildWarEndpoint : Endpoint<SetCurrentGuildWarRequest, GuildWarResponse>
{
    public override void Configure()
    {
        Put("guild-wars/current");
        Summary(summary =>
        {
            summary.Summary = "Sets the current war's season and battlefield level, creating the war when there is none.";
            summary.Description = "revision is ignored when the war is created. Changing the level keeps the rarityCap "
                + "of attacks already logged.";
            summary.Response<GuildWarResponse>(StatusCodes.Status200OK, "The current war, as GET returns it.");
            summary.Response(StatusCodes.Status400BadRequest,
                "guildWarUnknownSeason, guildWarUnknownLevel, or an invalid shape.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
            summary.Response(StatusCodes.Status409Conflict, "guildWarRevisionStale; current is the war as it stands.");
        });
    }

    public override async Task HandleAsync(SetCurrentGuildWarRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var service = Resolve<GuildWarsService>();
        if (GuildWarRules.SettingsProblem(service.Facts, req.SeasonId, req.BattlefieldLevel) is { } problem)
        {
            await GuildWarProblems.SendSettingsProblemAsync(this, problem, ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var now = GuildWarsService.Stored(Resolve<TimeProvider>().GetUtcNow());
        var (stale, war) = await service.ExecuteLockedAsync(async transaction =>
        {
            var current = await service.CurrentAsync(tracking: true, ct);
            if (current is null)
            {
                current = GuildWar.Start(profileId, req.SeasonId, req.BattlefieldLevel, now);
                db.GuildWars.Add(current);
            }
            else if (current.Revision != req.Revision)
            {
                return (true, current);
            }
            else
            {
                current.SeasonId = req.SeasonId;
                current.BattlefieldLevel = req.BattlefieldLevel;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (false, current);
        }, ct);

        if (stale)
        {
            await this.SendConflictAsync(
                GuildWarsService.RevisionStale, "The war changed on another device. Refresh and try again.",
                GuildWarResponse.From(war), ct);
            return;
        }

        await Send.OkAsync(GuildWarResponse.From(war), ct);
    }
}

public sealed class StartGuildWarEndpoint : Endpoint<StartGuildWarRequest, GuildWarResponse>
{
    public override void Configure()
    {
        Post("guild-wars");
        Summary(summary =>
        {
            summary.Summary = "Ends the current war, keeping it and its attacks, and starts a new one.";
            summary.Response<GuildWarResponse>(StatusCodes.Status200OK, "The new current war, with no attacks.");
            summary.Response(StatusCodes.Status400BadRequest,
                "guildWarUnknownSeason, guildWarUnknownLevel, or an invalid shape.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
        });
    }

    public override async Task HandleAsync(StartGuildWarRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var service = Resolve<GuildWarsService>();
        if (GuildWarRules.SettingsProblem(service.Facts, req.SeasonId, req.BattlefieldLevel) is { } problem)
        {
            await GuildWarProblems.SendSettingsProblemAsync(this, problem, ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var now = GuildWarsService.Stored(Resolve<TimeProvider>().GetUtcNow());
        var war = await service.ExecuteLockedAsync(async transaction =>
        {
            // Two saves: the ended war leaves the one-current-war index before the new one enters it.
            if (await service.CurrentAsync(tracking: true, ct) is { } ended)
            {
                ended.EndedAt = now;
                await db.SaveChangesAsync(ct);
            }

            var started = GuildWar.Start(profileId, req.SeasonId, req.BattlefieldLevel, now);
            db.GuildWars.Add(started);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return started;
        }, ct);

        await Send.OkAsync(GuildWarResponse.From(war), ct);
    }
}

public sealed class ListGuildWarsEndpoint : Endpoint<ListGuildWarsRequest, PastGuildWarsResponse>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public override void Configure()
    {
        Get("guild-wars");
        Summary(summary =>
        {
            summary.Summary = "Reads a page of the caller's ended wars, newest first, each with its attacks.";
            summary.Description = "Ordered by startedAt then id, both descending; the current war is not listed. limit "
                + "is 1–50 (default 20); after is the opaque nextAfter of the previous page, null on the last one.";
            summary.Response<PastGuildWarsResponse>(StatusCodes.Status200OK);
            summary.Response(StatusCodes.Status400BadRequest, "limit is out of range or after is not a valid cursor.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
        });
    }

    public override async Task HandleAsync(ListGuildWarsRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var limit = req.Limit ?? DefaultLimit;
        if (limit is < 1 or > MaxLimit)
            AddError(new ValidationFailure("limit", $"limit must be between 1 and {MaxLimit}."));
        RunRecordCursor? after = null;
        if (req.After is { Length: > 0 } afterValue && !RunRecordCursor.TryDecode(afterValue, out after))
            AddError(new ValidationFailure("after", "after is not a valid cursor; pass the nextAfter of the previous page."));
        if (ValidationFailed)
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        // Scoped to the caller's profile by PlannerDbContext's global query filter; served by
        // ix_guild_wars_profile_id_started_at_id.
        var db = Resolve<PlannerDbContext>();
        var wars = after is { } cursor
            ? db.GuildWars.FromSql(
                $"SELECT * FROM guild_wars WHERE (started_at, id) < ({cursor.StartedAt}, {cursor.Id})")
            : db.GuildWars;
        var page = await wars
            .AsNoTracking()
            .Where(war => war.EndedAt != null)
            .OrderByDescending(war => war.StartedAt)
            .ThenByDescending(war => war.Id)
            .Take(limit + 1)
            .Include(war => war.Attacks)
            .AsSplitQuery()
            .ToListAsync(ct);

        var hasMore = page.Count > limit;
        var items = page.Take(limit).ToList();
        await Send.OkAsync(
            new PastGuildWarsResponse(
                [.. items.Select(GuildWarResponse.From)],
                hasMore ? new RunRecordCursor(items[^1].StartedAt, items[^1].Id.Value).Encode() : null),
            ct);
    }
}

public sealed class CreateGuildWarAttackEndpoint : Endpoint<CreateGuildWarAttackRequest, GuildWarResponse>
{
    public override void Configure()
    {
        Post("guild-wars/current/attacks");
        Summary(summary =>
        {
            summary.Summary = "Logs an attack in the current war.";
            summary.Description = "The server derives rarityCap from the zone, the slot and the war's battlefield level. "
                + "attackedAt defaults to now.";
            summary.Response<GuildWarResponse>(StatusCodes.Status200OK, "The current war, as GET returns it.");
            summary.Response(StatusCodes.Status400BadRequest,
                "The first broken rule (guildWarUnknownZone, guildWarUnknownUnit, guildWarUnitDeployed, "
                + "guildWarTokensSpent), or an invalid shape.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The caller has no profile.");
            summary.Response(StatusCodes.Status409Conflict, "guildWarNotStarted: the caller has no current war.");
        });
    }

    public override async Task HandleAsync(CreateGuildWarAttackRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var service = Resolve<GuildWarsService>();
        var facts = service.Facts;
        var draft = req.ToDraft();
        var db = Resolve<PlannerDbContext>();
        var now = GuildWarsService.Stored(Resolve<TimeProvider>().GetUtcNow());
        var (war, broken) = await service.ExecuteLockedAsync<(GuildWar?, GuildWarAttackRuleCode?)>(async transaction =>
        {
            var current = await service.CurrentAsync(tracking: true, ct);
            if (current is null)
                return (null, null);
            if (GuildWarRules.Evaluate(draft, current, null, facts) is { } code)
                return (current, code);

            db.GuildWarAttacks.Add(new GuildWarAttack
            {
                Id = GuildWarAttackId.From(Guid.CreateVersion7()),
                WarId = current.Id,
                ProfileId = profileId,
                AttackedAt = req.AttackedAt is { } attackedAt ? GuildWarsService.Stored(attackedAt) : now,
                ZoneId = draft.ZoneId,
                Slot = draft.Slot,
                RarityCap = GuildWarRules.RarityCap(facts, current, draft.ZoneId, draft.Slot)!.Value,
                Lineup = draft.Lineup,
                Result = req.Result,
                UnitsLost = req.UnitsLost,
                Enemy = draft.Enemy,
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return (current, null);
        }, ct);

        if (war is null)
        {
            await GuildWarProblems.SendNotStartedAsync(this, ct);
            return;
        }

        if (broken is { } rule)
        {
            await GuildWarProblems.SendRuleBrokenAsync(this, rule, ct);
            return;
        }

        await Send.OkAsync(GuildWarResponse.From(war), ct);
    }
}

public sealed class UpdateGuildWarAttackEndpoint : Endpoint<UpdateGuildWarAttackRequest, GuildWarResponse>
{
    public override void Configure()
    {
        Put("guild-wars/{warId}/attacks/{attackId}");
        Summary(summary =>
        {
            summary.Summary = "Replaces an attack's fields when the revision matches.";
            summary.Description = "An attack of an ended war may be edited. rarityCap is derived again only when the "
                + "zone or slot changes; attackedAt is kept.";
            summary.Response<GuildWarResponse>(StatusCodes.Status200OK, "The attack's war, as GET returns it.");
            summary.Response(StatusCodes.Status400BadRequest,
                "The first broken rule (guildWarUnknownZone, guildWarUnknownUnit, guildWarUnitDeployed), or an "
                + "invalid shape.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The war or attack does not exist or belongs to another profile.");
            summary.Response(StatusCodes.Status409Conflict,
                "guildWarAttackRevisionStale; current is the attack's war as it stands.");
        });
    }

    public override async Task HandleAsync(UpdateGuildWarAttackRequest req, CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null
            || GuildWarProblems.RouteWarId(this) is not { } warId
            || GuildWarProblems.RouteAttackId(this) is not { } attackId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var service = Resolve<GuildWarsService>();
        var facts = service.Facts;
        var draft = req.ToDraft();
        var db = Resolve<PlannerDbContext>();
        var (outcome, war, broken) = await service.ExecuteLockedAsync<(UpdateOutcome, GuildWar?, GuildWarAttackRuleCode?)>(
            async transaction =>
            {
                var current = await service.FindTrackedAsync(warId, ct);
                var attack = current?.Attacks.FirstOrDefault(entity => entity.Id == attackId);
                if (current is null || attack is null)
                    return (UpdateOutcome.NotFound, null, null);
                if (attack.Revision != req.Revision)
                    return (UpdateOutcome.Stale, current, null);
                if (GuildWarRules.Evaluate(draft, current, attackId, facts) is { } code)
                    return (UpdateOutcome.RulesBroken, current, code);

                if (attack.ZoneId != draft.ZoneId || attack.Slot != draft.Slot)
                    attack.RarityCap = GuildWarRules.RarityCap(facts, current, draft.ZoneId, draft.Slot)!.Value;
                attack.ZoneId = draft.ZoneId;
                attack.Slot = draft.Slot;
                attack.Lineup = draft.Lineup;
                attack.Result = req.Result;
                attack.UnitsLost = req.UnitsLost;
                attack.Enemy = draft.Enemy;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return (UpdateOutcome.Updated, current, null);
            }, ct);

        switch (outcome)
        {
            case UpdateOutcome.NotFound:
                await this.SendNotFoundProblemAsync(ct);
                return;
            case UpdateOutcome.Stale:
                await this.SendConflictAsync(
                    GuildWarsService.AttackRevisionStale, "The attack changed on another device. Refresh and try again.",
                    GuildWarResponse.From(war!), ct);
                return;
            case UpdateOutcome.RulesBroken:
                await GuildWarProblems.SendRuleBrokenAsync(this, broken!.Value, ct);
                return;
            default:
                await Send.OkAsync(GuildWarResponse.From(war!), ct);
                return;
        }
    }

    private enum UpdateOutcome
    {
        Updated,
        NotFound,
        Stale,
        RulesBroken,
    }
}

public sealed class DeleteGuildWarAttackEndpoint : EndpointWithoutRequest<GuildWarResponse>
{
    public override void Configure()
    {
        Delete("guild-wars/{warId}/attacks/{attackId}");
        Summary(summary =>
        {
            summary.Summary = "Removes an attack (Undo), freeing its token and units.";
            summary.Response<GuildWarResponse>(StatusCodes.Status200OK, "The attack's war, as GET returns it.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The war or attack does not exist or belongs to another profile.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is null
            || GuildWarProblems.RouteWarId(this) is not { } warId
            || GuildWarProblems.RouteAttackId(this) is not { } attackId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var service = Resolve<GuildWarsService>();
        var db = Resolve<PlannerDbContext>();
        var war = await service.ExecuteLockedAsync(async transaction =>
        {
            var current = await service.FindTrackedAsync(warId, ct);
            var attack = current?.Attacks.FirstOrDefault(entity => entity.Id == attackId);
            if (current is null || attack is null)
                return null;

            current.Attacks.Remove(attack);
            db.GuildWarAttacks.Remove(attack);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return current;
        }, ct);

        if (war is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        await Send.OkAsync(GuildWarResponse.From(war), ct);
    }
}
