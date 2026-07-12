using Refit;
using TacticusPlanner.TacticusApi.Models.Guild;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.TacticusApi;

public interface ITacticusApi
{
    [Get("/api/v1/player")]
    Task<PlayerResponse> GetPlayerAsync(
        [Header("X-API-KEY")] string personalApiToken,
        CancellationToken cancellationToken = default
    );

    [Get("/api/v1/guild")]
    Task<GuildResponse> GetGuildAsync(
        [Header("X-API-KEY")] string guildApiToken,
        CancellationToken cancellationToken = default
    );

    [Get("/api/v1/guildRaid")]
    Task<GuildRaidResponse> GetGuildRaidsAsync(
        [Header("X-API-KEY")] string guildApiToken,
        CancellationToken cancellationToken = default
    );

    [Get("/api/v1/guildRaid/{season}")]
    Task<GuildRaidResponse> GetGuildRaidBySeasonAsync(
        [Header("X-API-KEY")] string guildApiToken,
        int season,
        CancellationToken cancellationToken = default
    );
}
