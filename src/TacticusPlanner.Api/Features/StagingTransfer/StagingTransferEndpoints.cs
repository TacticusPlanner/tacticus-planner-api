using System.Security.Cryptography;
using System.Text;
using FastEndpoints;
using FluentValidation;
using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.Auth;
using TacticusPlanner.Api.Features.UpstreamRateLimiting;
using TacticusPlanner.Api.Http;

namespace TacticusPlanner.Api.Features.StagingTransfer;

public sealed record CreateStagingTransferCodeResponse(string Code, DateTimeOffset ExpiresAt);

/// <summary>Production: mints a code the signed-in tester pastes into staging to copy their data there.</summary>
public sealed class CreateStagingTransferCodeEndpoint : EndpointWithoutRequest<CreateStagingTransferCodeResponse>
{
    public override void Configure()
    {
        Post("account/staging-transfer-code");
        Summary(summary =>
        {
            summary.Summary = "Creates a short-lived code that copies this account's planner data into staging.";
            summary.Description = "Production only (404 elsewhere). "
                + "The code is valid for 10 minutes and is redeemed on staging with "
                + "POST account/import-from-production. It carries no data itself.";
            summary.Response(StatusCodes.Status404NotFound, "Not available in this environment, or no profile.");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (!Resolve<IOptions<StagingTransferOptions>>().Value.IsSource || profileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var (code, expiresAt) = Resolve<StagingTransferCodeService>().Mint(profileId.Value);
        await Send.OkAsync(new CreateStagingTransferCodeResponse(code, expiresAt), ct);
    }
}

public sealed record ExportForStagingRequest(string? Code);

/// <summary>
/// Production, service-to-service: staging redeems a transfer code here for the profile's bundle. Anonymous
/// at the user level (staging holds no production token) and authenticated by the shared service key instead.
/// Left out of the OpenAPI document (and so untyped here): the web app never calls it, and the response is a
/// <see cref="StagingTransferBundle"/>.
/// </summary>
public sealed class ExportForStagingEndpoint : Endpoint<ExportForStagingRequest>
{
    public const string Route = "internal/staging-transfer/export";

    public override void Configure()
    {
        Post(Route);
        AllowAnonymous();
        Description(builder => builder.ExcludeFromDescription());
    }

    public override async Task HandleAsync(ExportForStagingRequest req, CancellationToken ct)
    {
        var options = Resolve<IOptions<StagingTransferOptions>>().Value;
        if (!options.IsSource)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var presented = HttpContext.Request.Headers[StagingTransferOptions.ServiceKeyHeaderName].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented),
                Encoding.UTF8.GetBytes(options.ServiceKey!)))
        {
            await ApiProblems.WriteAsync(HttpContext, StatusCodes.Status401Unauthorized);
            return;
        }

        var bundle = Resolve<StagingTransferCodeService>().Read(req.Code) is { } profileId
            ? await Resolve<StagingTransferExporter>().ExportAsync(profileId, ct)
            : null;
        if (bundle is null)
        {
            AddError(request => request.Code!, StagingTransferErrors.InvalidCodeMessage);
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
            return;
        }

        await Send.OkAsync((object)bundle, ct);
    }
}

public sealed record ImportFromProductionRequest(string? Code);

public sealed class ImportFromProductionValidator : Validator<ImportFromProductionRequest>
{
    public ImportFromProductionValidator()
    {
        RuleFor(request => request.Code).NotEmpty().MaximumLength(1024);
    }
}

public sealed record ImportFromProductionResponse(
    int Goals,
    int Projects,
    int LegendaryEventPlans,
    int LegendaryEventRunRecords,
    int PersonalRosterSnapshots,
    bool Settings,
    bool Overrides);

/// <summary>Staging: redeems a production transfer code and replaces the caller's planner data with it.</summary>
public sealed class ImportFromProductionEndpoint : Endpoint<ImportFromProductionRequest, ImportFromProductionResponse>
{
    public override void Configure()
    {
        Post("account/import-from-production");
        Options(builder => builder.WithMetadata(UpstreamRateLimit.Slow));
        Summary(summary =>
        {
            summary.Summary = "Replaces this staging account's planner data with a copy from production.";
            summary.Description = "Staging only (404 elsewhere). "
                + "Redeems a code from production's POST account/staging-transfer-code and, in one transaction, "
                + "replaces goals, projects, Legendary Event plans and run records, personal roster snapshots, settings and progress "
                + "overrides. The Tacticus API key, player data and guild are not copied. 400 when the code is "
                + "invalid or expired; 502 when production cannot be reached.";
            summary.Response(StatusCodes.Status404NotFound, "Not available in this environment, or no profile.");
            summary.Response(StatusCodes.Status429TooManyRequests, UpstreamRateLimit.ResponseDescription);
            summary.Response(StatusCodes.Status502BadGateway, "Production could not be reached or refused the request.");
        });
    }

    public override async Task HandleAsync(ImportFromProductionRequest req, CancellationToken ct)
    {
        var profileId = ProcessorState<CurrentUserState>().ProfileId;
        if (!Resolve<IOptions<StagingTransferOptions>>().Value.IsTarget || profileId is null)
        {
            await this.SendNotFoundProblemAsync(ct);
            return;
        }

        var result = await Resolve<IStagingTransferSourceClient>().ExportAsync(req.Code!.Trim(), ct);
        switch (result)
        {
            case StagingTransferSourceResult.InvalidCode:
                AddError(request => request.Code!, StagingTransferErrors.InvalidCodeMessage);
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            case StagingTransferSourceResult.UnsupportedFormat:
                AddError(request => request.Code!, "This code can't be applied yet.");
                await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
                return;
            case StagingTransferSourceResult.Unavailable:
                await ApiProblems.WriteAsync(HttpContext, StatusCodes.Status502BadGateway);
                return;
        }

        var bundle = ((StagingTransferSourceResult.Success)result).Bundle;
        var counts = await Resolve<StagingTransferImporter>().ImportAsync(profileId.Value, bundle, ct);
        await Send.OkAsync(new ImportFromProductionResponse(
            counts.Goals,
            counts.Projects,
            counts.LegendaryEventPlans,
            counts.LegendaryEventRunRecords,
            counts.PersonalRosterSnapshots,
            counts.Settings,
            counts.Overrides), ct);
    }
}

internal static class StagingTransferErrors
{
    public const string InvalidCodeMessage = "The code is invalid or has expired.";
}
