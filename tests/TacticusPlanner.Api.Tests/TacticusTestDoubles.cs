using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.TacticusApi;
using TacticusPlanner.TacticusApi.Models.Guild;
using TacticusPlanner.TacticusApi.Models.GuildRaid;
using TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.Tests;

/// <summary>
/// Stands in for the upstream Tacticus game API so <see cref="TacticusApi.Features.TacticusIntegration.TacticusApiKeyValidator"/>
/// can be exercised without an outbound network call. <see cref="ValidKey"/> validates successfully; any other
/// value comes back as "invalid" (no player details), mirroring how the validator treats a 400/401/403/404 from
/// the real API.
/// </summary>
internal sealed class FakeTacticusApi : ITacticusApi
{
    public const string ValidKey = "valid-tacticus-api-key";
    public const string PlayerName = "TestPlayer";
    public const int PowerLevel = 12345;

    public Task<PlayerResponse> GetPlayerAsync(string personalApiToken, CancellationToken cancellationToken = default)
    {
        var details = personalApiToken == ValidKey
            ? new PlayerDetails { Name = PlayerName, PowerLevel = PowerLevel }
            : null;

        return Task.FromResult(new PlayerResponse { Player = new Player { Details = details! } });
    }

    public Task<GuildResponse> GetGuildAsync(string guildApiToken) => throw new NotSupportedException();

    public Task<GuildRaidResponse> GetGuildRaidsAsync(string guildApiToken) => throw new NotSupportedException();

    public Task<GuildRaidResponse> GetGuildRaidBySeasonAsync(string guildApiToken, int season) =>
        throw new NotSupportedException();
}

/// <summary>
/// Stands in for the legacy V1 planner backend so <see cref="ImportV1ProfileEndpoint"/> can be exercised without
/// an outbound network call.
/// </summary>
internal sealed class FakeTacticusV1Client : ITacticusV1Client
{
    public const string ValidUsername = "v1-user";
    public const string ValidPassword = "v1-password";
    public const string UsernameWithoutTacticusKey = "v1-user-no-key";
    public const string TacticusUserId = "v1-tacticus-user-id";

    private const string AccessToken = "v1-access-token-for-" + ValidUsername;
    private const string AccessTokenNoKey = "v1-access-token-for-" + UsernameWithoutTacticusKey;

    public Task<string?> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (password != ValidPassword)
        {
            return Task.FromResult<string?>(null);
        }

        return username switch
        {
            ValidUsername => Task.FromResult<string?>(AccessToken),
            UsernameWithoutTacticusKey => Task.FromResult<string?>(AccessTokenNoKey),
            _ => Task.FromResult<string?>(null),
        };
    }

    public Task<TacticusV1Profile?> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        return accessToken switch
        {
            AccessToken => Task.FromResult<TacticusV1Profile?>(
                new TacticusV1Profile(FakeTacticusApi.ValidKey, TacticusUserId)
            ),
            AccessTokenNoKey => Task.FromResult<TacticusV1Profile?>(new TacticusV1Profile(null, null)),
            _ => Task.FromResult<TacticusV1Profile?>(null),
        };
    }
}
