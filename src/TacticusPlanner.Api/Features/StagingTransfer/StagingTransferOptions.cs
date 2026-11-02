namespace TacticusPlanner.Api.Features.StagingTransfer;

/// <summary>
/// Configuration for copying a tester's production planner data into their staging account (see README
/// "Copy production data to staging"). The environment's role follows from which values are set:
/// <list type="bullet">
/// <item><b>Source</b> (production): <see cref="ServiceKey"/> only. Serves the transfer-code and export endpoints.</item>
/// <item><b>Target</b> (staging): <see cref="ServiceKey"/> and <see cref="SourceBaseUrl"/>. Serves the import endpoint.</item>
/// <item>Neither (local by default): every staging-transfer endpoint answers 404.</item>
/// </list>
/// </summary>
public sealed class StagingTransferOptions
{
    public const string SectionName = "StagingTransfer";

    /// <summary>
    /// <b>Secret.</b> Shared by production and staging. Staging sends it in <see cref="ServiceKeyHeaderName"/>
    /// when redeeming a transfer code; production refuses an export without it. It never authorizes which
    /// profile is exported: only a transfer code, minted and encrypted by production, does that.
    /// </summary>
    public string? ServiceKey { get; set; }

    /// <summary>Target only: base URL of the production API, e.g. <c>https://api.tacticusplanner.app</c>.</summary>
    public string? SourceBaseUrl { get; set; }

    public const string ServiceKeyHeaderName = "X-Staging-Transfer-Key";

    public bool IsSource => !string.IsNullOrWhiteSpace(ServiceKey) && string.IsNullOrWhiteSpace(SourceBaseUrl);

    public bool IsTarget => !string.IsNullOrWhiteSpace(ServiceKey) && !string.IsNullOrWhiteSpace(SourceBaseUrl);
}
