using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.GuildRaids.Enums;
using TacticusPlanner.Persistence;

namespace TacticusPlanner.Api.Features.Guilds;

public sealed class GetMyGuildRaidSeasonsEndpoint : EndpointWithoutRequest<GuildRaidSeasonsResponse>
{
    public override void Configure()
    {
        Get("guild/raid/seasons");
        Summary(summary =>
        {
            summary.Summary = "Lists the Guild Raid seasons held for the caller's guild.";
            summary.Description = "Returns season numbers and capture status only, newest first: no hits and no player "
                + "data. This endpoint never calls the upstream Guild Raid API.";
            summary.Response<GuildRaidSeasonsResponse>(StatusCodes.Status200OK, "The guild's known seasons.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated profile has not been provisioned.");
            summary.Response(StatusCodes.Status409Conflict, "No registered guild is linked (issueCode guildRaidNotReady).");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (ProcessorState<CurrentUserState>().ProfileId is not { } profileId)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        // Unique on profile_id: a profile is on at most one guild's roster.
        var membership = await db.GuildMembers.AsNoTracking()
            .Where(member => member.ProfileId == profileId)
            .Select(member => new { member.GuildId })
            .SingleOrDefaultAsync(ct);
        if (membership is null)
        {
            AddError("No linked registered guild is available.", "guildRaidNotReady");
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        var current = await db.GuildRaidSyncStates.AsNoTracking()
            .Where(state => state.GuildId == membership.GuildId && state.ActiveSeason != null)
            .Select(state => (int?)state.ActiveSeason!.SeasonNumber)
            .SingleOrDefaultAsync(ct);
        var stored = await db.GuildRaidSeasons.AsNoTracking()
            .Where(season => season.GuildId == membership.GuildId)
            .Select(season => new
            {
                season.SeasonNumber,
                season.SeasonConfigId,
                season.ObservedAt,
                HasHits = season.Attacks.Any(),
            })
            .ToDictionaryAsync(season => season.SeasonNumber, ct);
        var captures = await db.GuildRaidSeasonCaptures.AsNoTracking()
            .Where(capture => capture.GuildId == membership.GuildId)
            .ToDictionaryAsync(capture => capture.SeasonNumber, ct);

        var seasons = stored.Keys.Union(captures.Keys)
            .OrderDescending()
            .Select(number =>
            {
                var season = stored.GetValueOrDefault(number);
                var capture = captures.GetValueOrDefault(number);
                var status = number == current
                    ? GuildRaidSeasonStatus.Live
                    : capture?.Status switch
                    {
                        GuildRaidSeasonCaptureStatus.Final => GuildRaidSeasonStatus.Final,
                        GuildRaidSeasonCaptureStatus.Unavailable => GuildRaidSeasonStatus.Unavailable,
                        _ => GuildRaidSeasonStatus.Pending,
                    };
                return new GuildRaidSeasonResponse(
                    number,
                    season?.SeasonConfigId,
                    status,
                    season?.HasHits ?? false,
                    capture?.SkippedEntryCount ?? 0,
                    season?.ObservedAt,
                    status == GuildRaidSeasonStatus.Final ? capture?.FinalizedAt : null);
            })
            .ToList();

        await Send.OkAsync(new GuildRaidSeasonsResponse(current, seasons), ct);
    }
}

public sealed record GuildRaidSeasonsResponse(int? CurrentSeasonNumber, IReadOnlyList<GuildRaidSeasonResponse> Seasons);

public sealed record GuildRaidSeasonResponse(
    int SeasonNumber,
    string? SeasonConfigId,
    GuildRaidSeasonStatus Status,
    bool HasHits,
    int SkippedEntryCount,
    DateTimeOffset? ObservedAt,
    DateTimeOffset? FinalizedAt);

[JsonConverter(typeof(JsonStringEnumConverter<GuildRaidSeasonStatus>))]
public enum GuildRaidSeasonStatus
{
    [JsonStringEnumMemberName("live")] Live,
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("final")] Final,
    [JsonStringEnumMemberName("unavailable")] Unavailable,
}
