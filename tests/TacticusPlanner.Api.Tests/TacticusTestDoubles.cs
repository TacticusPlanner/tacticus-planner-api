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
