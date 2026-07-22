using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.Goals;
using TacticusPlanner.Api.Features.Guilds;
using TacticusPlanner.Api.Features.TacticusIntegration;
using TacticusPlanner.Api.Http;
using TacticusPlanner.Domain.PlayerData;
using TacticusPlanner.Domain.Profiles;
using TacticusPlanner.Persistence;
using TacticusPlanner.Persistence.Encryption;
using TacticusIntegrationEntity = TacticusPlanner.Domain.Profiles.TacticusIntegration;

namespace TacticusPlanner.Api.Features.V1Import;

public sealed class ImportV1ProfileEndpoint : Endpoint<ImportV1ProfileRequest, ImportV1ProfileResponse>
{
    public override void Configure()
    {
        Post("me/v1-import");
        Summary(summary =>
        {
            summary.Summary = "Selectively imports integration data, progress, and goals from V1.";
            summary.Description = "V1 credentials are used once and never persisted. After profile retrieval, "
                + "each selected part is applied independently and reports Imported, Skipped, or Failed. "
                + "Goals are translated into V2 create-goal specs (GoalSpecs) rather than created here — the "
                + "caller submits each spec through POST me/goals/combined, the same endpoint the regular "
                + "create-goal flow uses. A V1 goal is skipped when it can't be translated or when the account "
                + "already has a goal of that type for that entity.";
        });
    }

    public override async Task HandleAsync(ImportV1ProfileRequest req, CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (profileId is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var client = Resolve<ITacticusV1Client>();
        var accessToken = await client.LoginAsync(req.Username!.Trim(), req.Password!, ct);
        if (accessToken is null)
        {
            AddError(request => request.Password, "The V1 username or password is invalid.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var v1 = await client.GetProfileAsync(accessToken, ct);
        if (v1 is null)
        {
            AddError(request => request.Username, "The V1 profile could not be retrieved.");
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        var selection = req.Import!;
        var userId = selection.TacticusUserId
            ? await ImportUserIdAsync(profileId.Value, v1.TacticusUserId, ct)
            : ImportPartResult.NotSelected();
        var personalKey = selection.PersonalTacticusApiKey
            ? await ImportPersonalKeyAsync(profileId.Value, v1.TacticusApiKey, ct)
            : new PersonalKeyImportResult(ImportPartResult.NotSelected(), null, 0);
        var guild = selection.GuildApiToken
            ? await ImportGuildAsync(profileId.Value, v1.GuildApiKey, ct)
            : ImportPartResult.NotSelected();
        var onslaughtProgress = selection.OnslaughtProgress
            ? await ImportOnslaughtProgressAsync(profileId.Value, v1.OnslaughtProgress, ct)
            : ImportPartResult.NotSelected();
        var campaignEventProgress = selection.CampaignEventProgress
            ? await ImportCampaignEventProgressAsync(profileId.Value, v1.CampaignEventProgress, ct)
            : ImportPartResult.NotSelected();

        V1GoalImportResult goalResult;
        ImportPartResult goals;
        if (!selection.Goals)
        {
            goalResult = new V1GoalImportResult([], 0, []);
            goals = ImportPartResult.NotSelected();
        }
        else
        {
            goalResult = await Resolve<V1GoalImportService>().TranslateAsync(profileId.Value, v1.Goals, ct);
            goals = goalResult.GoalSpecs.Count > 0
                ? new ImportPartResult("Imported", null, null)
                : new ImportPartResult("Skipped", "no_importable_goals", "No supported V1 goals were available to import.");
        }

        await Send.OkAsync(new ImportV1ProfileResponse(
            userId,
            personalKey.Part,
            guild,
            onslaughtProgress,
            campaignEventProgress,
            goals,
            goalResult.GoalSpecs,
            goalResult.Skipped,
            goalResult.Issues
        )
        {
            ProfileId = profileId.Value.Value,
            PlayerName = personalKey.PlayerName,
            PowerLevel = personalKey.PowerLevel,
            TacticusApiKeyMasked = personalKey.Part.Status == "Imported"
                ? SecretMasker.Mask(v1.TacticusApiKey)
                : null,
            TacticusUserIdMasked = userId.Status == "Imported"
                ? SecretMasker.Mask(v1.TacticusUserId)
                : null,
        }, ct);
    }

    private async Task<ImportPartResult> ImportUserIdAsync(ProfileId profileId, string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ImportPartResult("Skipped", "missing_tacticus_user_id", "The V1 profile has no Tacticus User ID.");
        }

        var db = Resolve<PlannerDbContext>();
        var profile = await db.Profiles.FirstAsync(entity => entity.Id == profileId, ct);
        profile.TacticusUserId = TacticusUserId.From(value);
        profile.TacticusUserIdHash = Resolve<IColumnHashService>().ComputeHash(value);
        try
        {
            await db.SaveChangesAsync(ct);
            return new ImportPartResult("Imported", null, null);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new ImportPartResult("Failed", "tacticus_user_id_conflict", "The imported Tacticus User ID is already linked to another profile.");
        }
    }

    private async Task<PersonalKeyImportResult> ImportPersonalKeyAsync(
        ProfileId profileId,
        string? value,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new PersonalKeyImportResult(
                new ImportPartResult("Skipped", "missing_personal_api_key", "The V1 profile has no personal Tacticus API key."),
                null,
                0
            );
        }

        var validation = await Resolve<TacticusApiKeyValidator>().ValidateAsync(value, ct);
        if (validation is null)
        {
            return new PersonalKeyImportResult(
                new ImportPartResult("Failed", "personal_api_key_invalid", "The imported personal API key could not be validated."),
                null,
                0
            );
        }

        var db = Resolve<PlannerDbContext>();
        var integration = await db.TacticusIntegrations.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        if (integration is null)
        {
            integration = new TacticusIntegrationEntity { Id = profileId };
            db.TacticusIntegrations.Add(integration);
        }
        var now = Resolve<TimeProvider>().GetUtcNow();
        integration.TacticusApiKey = value;
        integration.TacticusSyncLastAttemptedAt = now;
        integration.TacticusSyncLastSucceededAt = now;
        await db.SaveChangesAsync(ct);
        return new PersonalKeyImportResult(
            new ImportPartResult("Imported", null, null),
            validation.PlayerName,
            validation.PowerLevel
        );
    }

    private async Task<ImportPartResult> ImportGuildAsync(ProfileId profileId, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ImportPartResult("Skipped", "missing_guild_api_token", "The V1 profile has no guild API token.");
        }

        var db = Resolve<PlannerDbContext>();
        var userId = await db.Profiles.Where(entity => entity.Id == profileId).Select(entity => entity.TacticusUserId).FirstAsync(ct);
        if (userId is null)
        {
            return new ImportPartResult("Failed", "missing_tacticus_user_id", "A Tacticus User ID is required before guild registration.");
        }

        var result = await Resolve<GuildSyncService>()
            .SynchronizeAsync(profileId, userId.Value, token, persistToken: true, ct, persistTokenOnlyIfNew: true);
        return result switch
        {
            GuildSyncResult.Success { WasCreated: true } => new ImportPartResult("Imported", null, null),
            GuildSyncResult.Success => new ImportPartResult("Skipped", "guild_already_registered", "The guild is already registered."),
            _ => new ImportPartResult("Failed", result.GetType().Name, GetGuildFailureMessage(result)),
        };
    }

    private async Task<ImportPartResult> ImportOnslaughtProgressAsync(
        ProfileId profileId,
        V1OnslaughtImportData source,
        CancellationToken ct)
    {
        if (!source.IsPresent)
        {
            return new ImportPartResult(
                "Skipped",
                "missing_onslaught_progress",
                "The V1 profile has no Onslaught progress."
            );
        }

        if (source.Progress is null)
        {
            return new ImportPartResult(
                "Failed",
                "invalid_onslaught_progress",
                "The V1 Onslaught progress is incomplete or invalid."
            );
        }

        var db = Resolve<PlannerDbContext>();
        var overrides = await db.PlayerDataOverrides.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        if (overrides is null)
        {
            overrides = new PlayerDataOverride { Id = profileId };
            db.PlayerDataOverrides.Add(overrides);
        }

        overrides.OnslaughtProgressOverrides =
        [
            ToOnslaughtRecord("Imperial", source.Progress.Imperial),
            ToOnslaughtRecord("Xenos", source.Progress.Xenos),
            ToOnslaughtRecord("Chaos", source.Progress.Chaos),
        ];
        if (db.Entry(overrides).State != EntityState.Added)
        {
            // Replacing only a JSON-owned collection does not reliably mark its owner Modified.
            db.Entry(overrides).State = EntityState.Modified;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return new ImportPartResult("Imported", null, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new ImportPartResult(
                "Failed",
                "onslaught_progress_conflict",
                "Onslaught progress changed during import. Try again."
            );
        }
    }

    private static OnslaughtProgressOverrideRecord ToOnslaughtRecord(
        string alliance,
        V1OnslaughtAllianceProgress source) => new()
        {
            Alliance = alliance,
            Sector = source.Sector,
            Tier = source.Tier,
        };

    private async Task<ImportPartResult> ImportCampaignEventProgressAsync(
        ProfileId profileId,
        V1CampaignEventProgressImportData source,
        CancellationToken ct)
    {
        if (!source.IsPresent)
        {
            return new ImportPartResult(
                "Skipped",
                "missing_campaign_event_progress",
                "The V1 profile has no regular campaign-event progress to import.");
        }

        if (source.Progress is null)
        {
            return new ImportPartResult(
                "Failed",
                "invalid_campaign_event_progress",
                "The V1 campaign-event progress is invalid.");
        }

        var db = Resolve<PlannerDbContext>();
        var overrides = await db.PlayerDataOverrides.FirstOrDefaultAsync(entity => entity.Id == profileId, ct);
        if (overrides is null)
        {
            overrides = new PlayerDataOverride { Id = profileId };
            db.PlayerDataOverrides.Add(overrides);
        }

        foreach (var imported in source.Progress)
        {
            var existing = overrides.CampaignEventProgressOverrides.FirstOrDefault(item =>
                item.CampaignGroupId.Value == imported.CampaignGroupId
                && item.Type == imported.Type);
            if (existing is null)
            {
                overrides.CampaignEventProgressOverrides.Add(new CampaignEventProgressOverrideRecord
                {
                    CampaignGroupId = CampaignId.From(imported.CampaignGroupId),
                    Type = imported.Type,
                    CompletedBattleCount = imported.CompletedBattleCount,
                });
            }
            else
            {
                existing.CompletedBattleCount = imported.CompletedBattleCount;
            }
        }

        if (db.Entry(overrides).State != EntityState.Added)
        {
            db.Entry(overrides).State = EntityState.Modified;
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return new ImportPartResult(
                "Imported",
                "challenge_progress_not_imported",
                "Regular event progress was imported. V1 challenge counts were not imported because exact battles are unknown.");
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new ImportPartResult(
                "Failed",
                "campaign_event_progress_conflict",
                "Campaign-event progress changed during import. Try again.");
        }
    }

    private static string GetGuildFailureMessage(GuildSyncResult result) => result switch
    {
        GuildSyncResult.InvalidRequest value => value.Message,
        GuildSyncResult.UpstreamRejected value => value.Message,
        GuildSyncResult.UpstreamUnavailable value => value.Message,
        GuildSyncResult.InvalidUpstreamData value => value.Message,
        GuildSyncResult.CallerNotAuthorized value => value.Message,
        GuildSyncResult.Conflict value => value.Message,
        _ => "Guild registration failed.",
    };

    private sealed record PersonalKeyImportResult(
        ImportPartResult Part,
        string? PlayerName,
        int PowerLevel
    );
}

public sealed record ImportV1Selection(
    bool PersonalTacticusApiKey,
    bool TacticusUserId,
    bool GuildApiToken,
    bool Goals,
    bool OnslaughtProgress,
    bool CampaignEventProgress
);

public sealed record ImportV1ProfileRequest(string? Username, string? Password, ImportV1Selection? Import);

public sealed record ImportPartResult(string Status, string? Code, string? Message)
{
    public static ImportPartResult NotSelected() => new("Skipped", "not_selected", null);
}

public sealed record ImportV1ProfileResponse(
    ImportPartResult TacticusUserId,
    ImportPartResult PersonalTacticusApiKey,
    ImportPartResult GuildApiToken,
    ImportPartResult OnslaughtProgress,
    ImportPartResult CampaignEventProgress,
    ImportPartResult Goals,
    // Parsed V1 goals, already shaped as create requests — one per unit. The caller submits each of
    // these through POST me/goals/combined; this endpoint no longer creates goals itself.
    IReadOnlyList<CreateCombinedGoalsRequest> GoalSpecs,
    int GoalsSkipped,
    IReadOnlyList<V1ImportIssue> GoalIssues
)
{
    public Guid ProfileId { get; init; }
    public string? PlayerName { get; init; }
    public int PowerLevel { get; init; }
    public string? TacticusApiKeyMasked { get; init; }
    public string? TacticusUserIdMasked { get; init; }
}
