using System.Collections.Concurrent;
using System.Net;
using Refit;
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
    // Guild scenarios are configured per-test (keyed by the guild API token each test makes up), unlike the
    // fixed Valid/ValidV2 player-sync constants above — Guild Phase 1 tests need far more varied rosters
    // (roles, member counts, malformed data) than a couple of hardcoded fixtures could express. Tokens are
    // unique per test (typically Guid-derived), so no cross-test cleanup is required.
    private static readonly ConcurrentDictionary<string, GuildResponse> GuildResponsesByToken = new();
    private static readonly ConcurrentDictionary<string, HttpStatusCode> GuildRejectionsByToken = new();
    private static readonly ConcurrentDictionary<string, bool> GuildUnavailableTokens = new();

    /// <summary>Registers the <see cref="GuildResponse"/> <see cref="GetGuildAsync"/> returns for
    /// <paramref name="guildApiToken"/>. Build the response via <see cref="BuildGuildResponse"/>.</summary>
    public static void ConfigureGuildResponse(string guildApiToken, GuildResponse response) =>
        GuildResponsesByToken[guildApiToken] = response;

    /// <summary>Makes <see cref="GetGuildAsync"/> throw a Refit <see cref="ApiException"/> with the given
    /// status for <paramref name="guildApiToken"/> — simulates the Tacticus API rejecting a bad/expired/
    /// wrong-scope token (mirrors <see cref="GuildSyncService"/>'s 400/401/403/404 handling).</summary>
    public static void ConfigureGuildRejection(string guildApiToken, HttpStatusCode statusCode) =>
        GuildRejectionsByToken[guildApiToken] = statusCode;

    /// <summary>Makes <see cref="GetGuildAsync"/> throw an <see cref="HttpRequestException"/> for
    /// <paramref name="guildApiToken"/> — simulates the Tacticus API being unreachable.</summary>
    public static void ConfigureGuildUnavailable(string guildApiToken) =>
        GuildUnavailableTokens[guildApiToken] = true;

    /// <summary>Builds a <see cref="GuildResponse"/> for <see cref="ConfigureGuildResponse"/> from a
    /// simple member tuple list, so tests don't have to construct the nested Tacticus wire types by hand.</summary>
    public static GuildResponse BuildGuildResponse(
        Guid guildId,
        string tag,
        string name,
        int level,
        params (Guid UserId, GuildRole Role, int Level, long? LastActivityOn)[] members
    )
    {
        return new GuildResponse
        {
            Guild = new Guild
            {
                GuildId = guildId,
                GuildTag = tag,
                Name = name,
                Level = level,
                Members = members
                    .Select(member => new TacticusApi.Models.Guild.GuildMember
                    {
                        UserId = member.UserId,
                        Role = member.Role,
                        Level = member.Level,
                        LastActivityOn = member.LastActivityOn,
                    })
                    .ToList(),
            },
        };
    }

    private static async Task<ApiException> CreateGuildApiExceptionAsync(HttpStatusCode statusCode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tacticusgame.com/guild");
        using var response = new HttpResponseMessage(statusCode) { RequestMessage = request };
        return await ApiException.Create(request, HttpMethod.Post, response, new RefitSettings());
    }

    public const string ValidKey = "valid-tacticus-api-key";
    public const string PlayerName = "TestPlayer";
    public const int PowerLevel = 12345;

    /// <summary>Same player identity as <see cref="ValidKey"/> but a different <c>configHash</c> and slightly
    /// different unit/campaign data, so player-sync tests can exercise the "data changed" path deterministically
    /// (without any shared mutable state between tests).</summary>
    public const string ValidKeyV2 = "valid-tacticus-api-key-v2";

    public const string ConfigHashV1 = "config-hash-v1";
    public const string ConfigHashV2 = "config-hash-v2";

    /// <summary>Real catalog character id (Ultramarines core character, see campaign-battles-indomitus.json).</summary>
    public const string CharacterUnitId = "ultraTigurius";

    /// <summary>Catalog campaign group id realigned to the Tacticus API's own id (see ADR 0007 / GameCatalogDatasets).</summary>
    public const string CampaignId = "campaign1";

    /// <summary>A campaign-event id — also a real catalog groupId (see GameCatalogDatasets.CampaignBattleGroups'
    /// remarks: eventCampaign1 -> death-guard-vs-admech).</summary>
    public const string EventCampaignId = "eventCampaign1";

    public Task<PlayerResponse> GetPlayerAsync(string personalApiToken, CancellationToken cancellationToken = default)
    {
        var isV2 = personalApiToken == ValidKeyV2;
        var isValid = personalApiToken == ValidKey || isV2;

        var details = isValid
            ? new PlayerDetails { Name = PlayerName, PowerLevel = PowerLevel }
            : null;

        if (!isValid)
        {
            return Task.FromResult(new PlayerResponse { Player = new Player { Details = details! } });
        }

        var unit = new Unit
        {
            Id = CharacterUnitId,
            Name = "Tigurius",
            ProgressionIndex = isV2 ? 12 : 11,
            Xp = isV2 ? 210000 : 200000,
            XpLevel = 35,
            Rank = isV2 ? 13 : 12,
            Shards = 59,
            MythicShards = 0,
            Abilities = [new Ability { Id = "StormOfWrath", Level = 35 }],
            Upgrades = [0, 2, 4],
            Items = [],
        };

        var campaign = new CampaignProgress
        {
            Id = CampaignId,
            Name = "Indomitus",
            Type = "Standard",
            Battles = [new CampaignLevel { BattleIndex = 0, AttemptsLeft = 3, AttemptsUsed = 0 }],
        };

        var eventCampaign = new CampaignProgress
        {
            Id = EventCampaignId,
            Name = string.Empty,
            Type = "Standard",
            Battles = [new CampaignLevel { BattleIndex = 0, AttemptsLeft = 10, AttemptsUsed = 0 }],
        };

        var response = new PlayerResponse
        {
            Player = new Player
            {
                Details = details!,
                Units = [unit],
                Inventory = new Inventory
                {
                    Upgrades = [],
                    Shards = [],
                    MythicShards = [],
                    XpBooks = [],
                    AbilityBadges = new AbilityBadges { Imperial = [], Xenos = [], Chaos = [] },
                    Components = [],
                    ForgeBadges = [],
                    Orbs = new Orbs { Imperial = [], Xenos = [], Chaos = [] },
                    Items = [],
                    RequisitionOrders = new RequisitionOrders { Regular = 5, Blessed = 1 },
                    ResetStones = 2,
                },
                Progress = new Progress
                {
                    Campaigns = [campaign, eventCampaign],
                    LegendaryEvents = [],
                },
            },
            Metadata = new Metadata
            {
                ConfigHash = isV2 ? ConfigHashV2 : ConfigHashV1,
                LastUpdatedOn = 1_780_000_000,
                Scopes = ["Player"],
            },
        };

        return Task.FromResult(response);
    }

    public async Task<GuildResponse> GetGuildAsync(string guildApiToken, CancellationToken cancellationToken = default)
    {
        if (GuildUnavailableTokens.ContainsKey(guildApiToken))
        {
            throw new HttpRequestException("Simulated Tacticus API outage.");
        }

        if (GuildRejectionsByToken.TryGetValue(guildApiToken, out var statusCode))
        {
            throw await CreateGuildApiExceptionAsync(statusCode);
        }

        if (GuildResponsesByToken.TryGetValue(guildApiToken, out var response))
        {
            return response;
        }

        throw new InvalidOperationException(
            $"No guild response configured for token '{guildApiToken}'. Call FakeTacticusApi.ConfigureGuildResponse first."
        );
    }

    public Task<GuildRaidResponse> GetGuildRaidsAsync(string guildApiToken, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<GuildRaidResponse> GetGuildRaidBySeasonAsync(
        string guildApiToken,
        int season,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();
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
    public const string UsernameWithGoals = "v1-user-with-goals";
    public const string TacticusUserId = "v1-tacticus-user-id";

    private const string AccessToken = "v1-access-token-for-" + ValidUsername;
    private const string AccessTokenNoKey = "v1-access-token-for-" + UsernameWithoutTacticusKey;
    private const string AccessTokenWithGoals = "v1-access-token-for-" + UsernameWithGoals;

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
            UsernameWithGoals => Task.FromResult<string?>(AccessTokenWithGoals),
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
            AccessTokenWithGoals => Task.FromResult<TacticusV1Profile?>(
                new TacticusV1Profile(
                    null,
                    null,
                    null,
                    [
                        new V1Goal(
                            "rank-1",
                            "Bellator",
                            1,
                            1,
                            true,
                            "Imported note",
                            1,
                            false,
                            0,
                            3,
                            false,
                            0,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null
                        ),
                        new V1Goal(
                            "material-1",
                            "Bellator",
                            6,
                            2,
                            false,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null
                        ),
                    ]
                )
            ),
            _ => Task.FromResult<TacticusV1Profile?>(null),
        };
    }
}
