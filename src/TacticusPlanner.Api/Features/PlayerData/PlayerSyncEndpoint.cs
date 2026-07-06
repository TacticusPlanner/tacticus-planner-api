using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Refit;
using TacticusPlanner.Persistence;
using TacticusPlanner.TacticusApi;
using PlayerDataSnapshotEntity = TacticusPlanner.Persistence.Users.PlayerData.PlayerDataSnapshot;

namespace TacticusPlanner.Api.Features.PlayerData;

/// <summary>
/// Syncs the authenticated profile's player data from the Tacticus API: fetches the player endpoint,
/// transforms and normalizes the response (never storing it raw — ADR 0007), and persists it as a
/// <see cref="PlayerDataSnapshotEntity"/> unless the incoming <c>configHash</c> matches what is already
/// stored, in which case persistence is skipped and the current manifest is returned unchanged.
/// </summary>
public sealed class PlayerSyncEndpoint(ITacticusApi tacticusApi, PlayerDataTransformer transformer)
    : EndpointWithoutRequest<PlayerDataManifest>
{
    public override void Configure()
    {
        Post("tacticus-integration/player-sync");
        Summary(summary =>
        {
            summary.Summary = "Syncs the authenticated user's player data from the Tacticus API.";
            summary.Description = "Fetches the current player from the Tacticus API, transforms it into "
                + "normalized chunks, and persists it. If the incoming configHash matches the last synced "
                + "value, persistence is skipped and the current manifest is returned unchanged.";
            summary.Response<PlayerDataManifest>(StatusCodes.Status200OK, "The current player-data manifest.");
            summary.Response(StatusCodes.Status400BadRequest, "No Tacticus API key is configured, or the Tacticus API rejected it.");
            summary.Response(StatusCodes.Status401Unauthorized, "The request is missing required identity claims.");
            summary.Response(StatusCodes.Status404NotFound, "The authenticated user has not signed up yet.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var issuer = User.FindFirstValue("iss");
        var subject = User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var db = Resolve<PlannerDbContext>();
        var account = await db.Accounts
            .Include(entity => entity.Profile)
            .ThenInclude(entity => entity!.TacticusIntegration)
            .Include(entity => entity.Profile)
            .ThenInclude(entity => entity!.PlayerDataSnapshot)
            .SingleOrDefaultAsync(entity => entity.Issuer == issuer && entity.Subject == subject, ct);

        var profile = account?.Profile;
        if (profile is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var apiKey = profile.TacticusIntegration?.TacticusApiKey;
        if (apiKey is null)
        {
            AddError("No Tacticus API key is configured for this profile.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var timeProvider = Resolve<TimeProvider>();
        var integration = profile.TacticusIntegration!;
        integration.TacticusSyncLastAttemptedAt = timeProvider.GetUtcNow();

        TacticusApi.Models.Player.PlayerResponse response;
        try
        {
            response = await tacticusApi.GetPlayerAsync(apiKey, ct);
        }
        catch (ApiException exception) when ((int)exception.StatusCode is 400 or 401 or 403 or 404)
        {
            await db.SaveChangesAsync(ct);
            AddError("The Tacticus API could not fetch player data for the configured key.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var incomingConfigHash = response.Metadata?.ConfigHash ?? string.Empty;
        var existing = profile.PlayerDataSnapshot;

        if (existing is not null && existing.ConfigHash == incomingConfigHash)
        {
            // Unchanged since the last sync: skip persistence entirely and return the current manifest.
            integration.TacticusSyncLastSucceededAt = timeProvider.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await Send.OkAsync(PlayerDataManifestBuilder.Build(existing), ct);
            return;
        }

        var transformed = transformer.Transform(response);
        var snapshot = existing ?? new PlayerDataSnapshotEntity { Id = profile.Id };

        snapshot.ConfigHash = transformed.ConfigHash;
        snapshot.TacticusLastUpdatedOn = transformed.TacticusLastUpdatedOn;
        snapshot.SourceHash = transformed.SourceHash;
        snapshot.SchemaVersion = PlayerDataTransformer.CurrentSchemaVersion;
        snapshot.SyncedAt = timeProvider.GetUtcNow();
        snapshot.ChunkHashes = transformed.ChunkHashes;
        snapshot.PlayerDetails = transformed.PlayerDetails;
        snapshot.Characters = transformed.Characters;
        snapshot.Mows = transformed.Mows;
        snapshot.InventoryUpgrades = transformed.InventoryUpgrades;
        snapshot.InventoryItems = transformed.InventoryItems;
        snapshot.Inventory = transformed.Inventory;
        snapshot.CampaignProgress = transformed.CampaignProgress;
        snapshot.CampaignEventsProgress = transformed.CampaignEventsProgress;
        snapshot.GameModeTokens = transformed.GameModeTokens;
        snapshot.LreProgress = transformed.LreProgress;

        if (existing is null)
        {
            db.PlayerDataSnapshots.Add(snapshot);
        }

        integration.TacticusSyncLastSucceededAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(PlayerDataManifestBuilder.Build(snapshot), ct);
    }
}
