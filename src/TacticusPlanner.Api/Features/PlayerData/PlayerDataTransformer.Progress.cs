using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.PlayerData.Chunks;
using TacticusApiPlayer = TacticusPlanner.TacticusApi.Models.Player;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>Campaign/live-progress/LRE mapping half of <see cref="PlayerDataTransformer"/>.</summary>
public sealed partial class PlayerDataTransformer
{
    /// <summary>
    /// The Tacticus API's own campaign ids follow two shapes: the always-available storyline chains
    /// (<c>campaignN</c>/<c>mirrorN</c>/<c>eliteN</c>/<c>eliteMirrorN</c>) and rotating limited-time events
    /// (<c>eventCampaignN</c>). This is the only reliable signal observed in a real response — there is no
    /// separate "is this an event" flag on the campaign progress payload.
    /// </summary>
    private static bool IsEventCampaign(string tacticusCampaignId) =>
        tacticusCampaignId.StartsWith("eventCampaign", StringComparison.Ordinal);

    private static CampaignProgressRecord MapCampaign(TacticusApiPlayer.CampaignProgress campaign) => new()
    {
        TacticusCampaignId = CampaignId.From(campaign.Id),
        Type = campaign.Type,
        HighestCompletedBattleIndex = HighestBattleIndex(campaign),
    };

    // Only attempts the player has actually spent are worth syncing/storing — an untouched battle
    // (AttemptsUsed == 0) carries no information beyond "not yet attempted", so keeping it out cuts
    // this often-changing chunk's size and churn.
    private static IEnumerable<BattleAttemptRecord> MapBattleAttempts(TacticusApiPlayer.CampaignProgress campaign) =>
        (campaign.Battles ?? [])
            .Where(battle => battle.AttemptsUsed > 0)
            .Select(battle => new BattleAttemptRecord
            {
                TacticusCampaignId = CampaignId.From(campaign.Id),
                BattleIndex = battle.BattleIndex,
                AttemptsLeft = battle.AttemptsLeft,
                AttemptsUsed = battle.AttemptsUsed,
            });

    private static int HighestBattleIndex(TacticusApiPlayer.CampaignProgress campaign)
    {
        var battles = campaign.Battles ?? [];
        return battles.Count == 0 ? -1 : battles.Max(battle => battle.BattleIndex);
    }

    private static GameModeTokensChunk MapGameModeTokens(TacticusApiPlayer.Progress? progress) => new()
    {
        Arena = MapTokenBucket(progress?.Arena?.Tokens),
        GuildRaid = progress?.GuildRaid is null
            ? null
            : new GuildRaidTokensRecord
            {
                Tokens = MapTokenBucket(progress.GuildRaid.Tokens) ?? new TokenBucketRecord(),
                BombTokens = MapTokenBucket(progress.GuildRaid.BombTokens) ?? new TokenBucketRecord(),
            },
        Onslaught = MapTokenBucket(progress?.Onslaught?.Tokens),
        SalvageRun = MapTokenBucket(progress?.SalvageRun?.Tokens),
    };

    private static TokenBucketRecord? MapTokenBucket(TacticusApiPlayer.TokenInfo? tokens) => tokens is null
        ? null
        : new TokenBucketRecord
        {
            Current = tokens.Current,
            Max = tokens.Max,
            NextTokenInSeconds = tokens.NextTokenInSeconds,
            RegenDelayInSeconds = tokens.RegenDelayInSeconds,
        };

    private static LreProgressRecord MapLre(TacticusApiPlayer.LegendaryEvent lre)
    {
        var lanesById = (lre.Lanes ?? []).ToDictionary(lane => lane.Id);

        return new LreProgressRecord
        {
            Id = UnitId.From(lre.Id),
            Alpha = MapLreTrack(lanesById.GetValueOrDefault(1)),
            Beta = MapLreTrack(lanesById.GetValueOrDefault(2)),
            Gamma = MapLreTrack(lanesById.GetValueOrDefault(3)),
            CurrentPoints = lre.CurrentPoints,
            CurrentCurrency = lre.CurrentCurrency,
            CurrentShards = lre.CurrentShards,
            CurrentClaimedChestIndex = lre.CurrentClaimedChestIndex,
            CurrentEventRun = lre.CurrentEvent?.Run,
            CurrentEventTokens = lre.CurrentEvent?.Tokens is { } tokens
                ? new TokenBucketRecord
                {
                    Current = tokens.CurrentTokens,
                    Max = tokens.MaxTokens,
                    NextTokenInSeconds = tokens.NextTokenInSeconds,
                    RegenDelayInSeconds = tokens.RegenDelayInSeconds,
                }
                : null,
            HasUsedAdForExtraTokenToday = lre.CurrentEvent?.HasUsedAdForExtraTokenToday,
            ExtraCurrencyPerPayout = lre.CurrentEvent?.ExtraCurrencyPerPayout,
        };
    }

    // Tacticus's lane ids 1/2/3 correspond to the catalog's named Alpha/Beta/Gamma tracks
    // (GameCatalogLreView) — mirrored here instead of a generic indexed lane list.
    private static LreTrackProgressRecord? MapLreTrack(TacticusApiPlayer.LegendaryEventLane? lane)
    {
        if (lane is null)
        {
            return null;
        }

        return new LreTrackProgressRecord
        {
            Encounters = (lane.Progress ?? [])
                .Select(progress => new LreEncounterProgressRecord
                {
                    ObjectivesCleared = (progress.ObjectivesCleared ?? []).ToList(),
                    HighScore = progress.HighScore,
                    EncounterPoints = progress.EncounterPoints,
                })
                .ToList(),
        };
    }
}
