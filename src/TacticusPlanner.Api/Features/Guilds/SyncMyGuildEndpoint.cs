using System.Globalization;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.UpstreamRateLimiting;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.Guilds;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

/// <summary>
/// Explicitly re-synchronizes the authenticated user's guild (Guild Phase 1). Requires the caller to be a
/// linked member of a registered guild with a stored token, persisted as Leader/Co-Leader (checked before any
/// upstream call), and to still be Leader/Co-Leader in the fresh upstream roster — a demoted or removed caller
/// is rejected and nothing is persisted (the persisted role only gates; it never authorizes on its own). At
/// most one upstream fetch per guild per <see cref="GuildSyncService.CooldownWindow"/>.
/// </summary>
public sealed class SyncMyGuildEndpoint : EndpointWithoutRequest<RegisteredGuildResponse>
{
    /// <summary>429 issue code while the guild's <see cref="GuildSyncService.CooldownWindow"/> is open.</summary>
    public const string CooldownIssueCode = "guildSyncCooldown";

    public override void Configure()
    {
        Post("guild/sync");
        Options(builder => builder.WithMetadata(UpstreamRateLimit.Slow));
        Summary(summary =>
        {
            summary.Summary = "Re-synchronizes the authenticated user's guild from the Tacticus API.";
            summary.Description = "Fetches fresh guild data using the stored Guild API token and requires "
                + "the caller to still appear as Leader or Co-Leader in that fresh response before any data "
                + "changes. The stored token itself is never replaced by this endpoint.";
            summary.Response<RegisteredGuildResponse>(StatusCodes.Status200OK, "The refreshed guild projection.");
            summary.Response(StatusCodes.Status400BadRequest, "The upstream guild response was malformed.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(
                StatusCodes.Status429TooManyRequests,
                UpstreamRateLimit.ResponseDescription + " Also " + CooldownIssueCode
                    + " (with Retry-After) while the guild was synchronized less than a minute ago by anyone."
            );
            summary.Response(
                StatusCodes.Status403Forbidden,
                "No linked/registered guild, no stored token, or the caller is not (or no longer) Leader/Co-Leader."
            );
            summary.Response(
                StatusCodes.Status404NotFound,
                "The authenticated account/profile has not been provisioned."
            );
            summary.Response(
                StatusCodes.Status409Conflict,
                "A database uniqueness conflict with an existing guild or membership."
            );
            summary.Response(StatusCodes.Status503ServiceUnavailable, "The Tacticus API is currently unavailable.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var state = ProcessorState<CurrentUserState>();
        if (state.ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();

        var profile = await db.Profiles
            .AsNoTracking()
            .Where(entity => entity.Id == profileId)
            .Select(entity => new { entity.TacticusUserId })
            .FirstOrDefaultAsync(ct);

        if (profile is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        if (profile.TacticusUserId is null)
        {
            AddError("A Tacticus User ID must be configured to synchronize this guild.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        // Unique on profile_id: a profile is on at most one guild's roster.
        var membership = await db.GuildMembers
            .AsNoTracking()
            .Where(entity => entity.ProfileId == profileId)
            .Select(entity => new { entity.GuildId, entity.Role, entity.Guild!.GuildApiToken })
            .SingleOrDefaultAsync(ct);

        if (membership is null)
        {
            AddError("No registered guild is linked to this profile.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        // Pre-check on the persisted role so plain members can't spend the shared guild token at all; the
        // fresh roster below stays the authority.
        if (membership.Role is not (GuildRole.Leader or GuildRole.CoLeader))
        {
            AddError("Only the guild's Leader or Co-Leader can synchronize it.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        if (membership.GuildApiToken is not { } storedToken)
        {
            AddError("No Guild API token is stored for this guild.");
            await Send.ErrorsAsync(StatusCodes.Status403Forbidden, ct);
            return;
        }

        // Claims the per-guild cooldown atomically before the upstream call, so concurrent or repeated
        // callers can't each pass the check; failed attempts count too, as for Guild Raid refresh.
        var timeProvider = Resolve<TimeProvider>();
        var now = timeProvider.GetUtcNow();
        var cooldownStart = now - GuildSyncService.CooldownWindow;
        var claimed = await db.Guilds
            .Where(entity => entity.Id == membership.GuildId
                && (entity.LastSyncAttemptedAt == null || entity.LastSyncAttemptedAt <= cooldownStart))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(entity => entity.LastSyncAttemptedAt, now)
                .SetProperty(entity => entity.Revision, entity => entity.Revision + 1), ct);
        if (claimed == 0)
        {
            var lastAttemptedAt = await db.Guilds
                .Where(entity => entity.Id == membership.GuildId)
                .Select(entity => entity.LastSyncAttemptedAt)
                .SingleAsync(ct);
            var retryAfter = Math.Max(1, (int)Math.Ceiling(
                (lastAttemptedAt!.Value + GuildSyncService.CooldownWindow - now).TotalSeconds));
            HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            await this.SendProblemAsync(
                StatusCodes.Status429TooManyRequests,
                ct,
                $"This guild was synchronized moments ago. Try again in {retryAfter} seconds.",
                CooldownIssueCode
            );
            return;
        }

        var result = await Resolve<GuildSyncService>()
            .SynchronizeAsync(profileId, profile.TacticusUserId.Value, storedToken, persistToken: false, ct: ct);

        if (result is GuildSyncResult.Success success)
        {
            await Send.OkAsync(GuildProjection.Build(success.Guild, success.CallerMember), ct);
            return;
        }

        AddError(GuildSyncResultMapper.GetMessage(result));
        await Send.ErrorsAsync(GuildSyncResultMapper.GetStatusCode(result), ct);
    }
}
