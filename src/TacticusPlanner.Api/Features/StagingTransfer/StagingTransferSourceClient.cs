using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TacticusPlanner.Api.Features.StagingTransfer;

public abstract record StagingTransferSourceResult
{
    public sealed record Success(StagingTransferBundle Bundle) : StagingTransferSourceResult;

    public sealed record InvalidCode : StagingTransferSourceResult;

    public sealed record UnsupportedFormat : StagingTransferSourceResult;

    public sealed record Unavailable : StagingTransferSourceResult;
}

/// <summary>Staging side: redeems a transfer code against production's export endpoint.</summary>
public interface IStagingTransferSourceClient
{
    Task<StagingTransferSourceResult> ExportAsync(string code, CancellationToken ct);
}

public sealed partial class StagingTransferSourceClient(
    IHttpClientFactory httpClientFactory,
    IOptions<StagingTransferOptions> options,
    ILogger<StagingTransferSourceClient> logger) : IStagingTransferSourceClient
{
    public const string HttpClientName = "staging-transfer-source";

    /// <summary>The same wire conventions as the API's own JSON (string enums, camelCase).</summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public async Task<StagingTransferSourceResult> ExportAsync(string code, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/" + ExportForStagingEndpoint.Route)
        {
            Content = JsonContent.Create(new ExportForStagingRequest(code), options: SerializerOptions),
        };
        request.Headers.Add(StagingTransferOptions.ServiceKeyHeaderName, options.Value.ServiceKey);

        try
        {
            using var response = await client.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return new StagingTransferSourceResult.InvalidCode();
            }

            if (!response.IsSuccessStatusCode)
            {
                LogExportRefused(logger, (int)response.StatusCode);
                return new StagingTransferSourceResult.Unavailable();
            }

            var bundle = await response.Content.ReadFromJsonAsync<StagingTransferBundle>(SerializerOptions, ct);
            if (bundle is null)
            {
                return new StagingTransferSourceResult.Unavailable();
            }

            return bundle.FormatVersion > StagingTransferBundle.CurrentFormatVersion
                ? new StagingTransferSourceResult.UnsupportedFormat()
                : new StagingTransferSourceResult.Success(bundle);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            LogExportFailed(logger, exception);
            return new StagingTransferSourceResult.Unavailable();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Production staging-transfer export answered {StatusCode}.")]
    private static partial void LogExportRefused(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Production staging-transfer export failed.")]
    private static partial void LogExportFailed(ILogger logger, Exception exception);
}
