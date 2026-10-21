using System.Text.RegularExpressions;
using FastEndpoints;
using FluentValidation.Results;
using Microsoft.AspNetCore.WebUtilities;
using ProblemDetails = Microsoft.AspNetCore.Mvc.ProblemDetails;

namespace TacticusPlanner.Api.Http;

/// <summary>
/// The single error shape of the API (<c>api-error-contract</c>): RFC 7807 problem details with
/// <c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c>, <c>instance</c>, <c>traceId</c> and <c>issueCode</c>,
/// plus <c>errors</c> on a validation failure and <c>current</c> on a conflict whose resource is known. Every
/// producer goes through <see cref="Complete"/>: the problem-details service (status-code pages for framework
/// 401/403/404/405 and <see cref="GlobalExceptionHandler"/>), the FastEndpoints error builder, and the explicit
/// sends below.
/// </summary>
public static partial class ApiProblems
{
    public const string ContentType = "application/problem+json";

    public const string Validation = "validation";
    public const string NotFound = "notFound";
    public const string Concurrency = "concurrency";
    public const string Internal = "internal";

    public static string IssueCodeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => Validation,
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => "methodNotAllowed",
        StatusCodes.Status409Conflict => Concurrency,
        StatusCodes.Status429TooManyRequests => "rateLimited",
        StatusCodes.Status502BadGateway => "upstreamRejected",
        StatusCodes.Status503ServiceUnavailable => "upstreamUnavailable",
        >= 500 => Internal,
        _ => "error",
    };

    /// <summary>Fills every required field the producer left empty; never overwrites one it set.</summary>
    public static void Complete(ProblemDetails problem, HttpContext http)
    {
        var status = problem.Status ??= http.Response.StatusCode;
        problem.Type ??= "about:blank";
        problem.Title ??= ReasonPhrases.GetReasonPhrase(status);
        problem.Detail ??= DefaultDetail(status);
        problem.Instance ??= http.Request.Path;
        problem.Extensions.TryAdd("traceId", http.TraceIdentifier);
        problem.Extensions.TryAdd("issueCode", IssueCodeFor(status));
    }

    private static string DefaultDetail(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "One or more validation errors occurred.",
        StatusCodes.Status401Unauthorized => "Sign in to continue.",
        StatusCodes.Status403Forbidden => "The request is not allowed.",
        StatusCodes.Status404NotFound => "The requested resource was not found.",
        StatusCodes.Status405MethodNotAllowed => "The method is not allowed for this resource.",
        StatusCodes.Status409Conflict => "The request conflicted with the current resource state.",
        StatusCodes.Status429TooManyRequests => "Too many requests. Try again later.",
        StatusCodes.Status502BadGateway => "The upstream service rejected the request.",
        StatusCodes.Status503ServiceUnavailable => "The upstream service is unavailable. Try again later.",
        _ => "The request failed. Use the traceId when contacting support.",
    };

    /// <summary>
    /// FastEndpoints' error builder (<c>Send.ErrorsAsync</c>, validators, binding failures). A failure's
    /// <c>ErrorCode</c>, when set, is the issue code; otherwise the status decides it. Every 4xx except 400 carries
    /// its (single, user-safe) message as <c>detail</c>; 400 carries the field map under <c>errors</c>.
    /// </summary>
    public static object FromFailures(List<ValidationFailure> failures, HttpContext http, int status)
    {
        var errors = failures
            .GroupBy(failure => FieldName(failure), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());
        var problem = status == StatusCodes.Status400BadRequest
            ? new HttpValidationProblemDetails(errors)
            : new ProblemDetails { Detail = failures.FirstOrDefault()?.ErrorMessage };
        problem.Status = status;
        if (status == StatusCodes.Status400BadRequest && failures is [var only])
            problem.Detail = only.ErrorMessage;
        if (failures.Select(failure => failure.ErrorCode).FirstOrDefault(IsIssueCode) is { } issueCode)
            problem.Extensions["issueCode"] = issueCode;
        Complete(problem, http);
        return problem;
    }

    // FluentValidation fills ErrorCode with its validator name (e.g. "NotEmptyValidator") when none is given.
    private static bool IsIssueCode(string? code) =>
        !string.IsNullOrEmpty(code) && !code.EndsWith("Validator", StringComparison.Ordinal);

    /// <summary>camelCase field path; a JSON binding failure is attributed to the property its message names
    /// (<c>Path: $.startPaused</c> → <c>startPaused</c>).</summary>
    private static string FieldName(ValidationFailure failure)
    {
        var name = failure.PropertyName;
        if (JsonPath().Match(failure.ErrorMessage) is { Success: true } path)
            name = path.Groups[1].Value;
        if (string.IsNullOrEmpty(name))
            return "general";
        return string.Join('.', name.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
    }

    [GeneratedRegex(@"Path: \$\.([A-Za-z0-9_\.\[\]]+)")]
    private static partial Regex JsonPath();

    /// <summary>Writes a problem through the shared problem-details service.</summary>
    public static async Task WriteAsync(
        HttpContext http, int status, string? issueCode = null, string? detail = null, object? current = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails { Status = status, Detail = detail };
        if (issueCode is not null)
            problem.Extensions["issueCode"] = issueCode;
        if (current is not null)
            problem.Extensions["current"] = current;
        foreach (var (key, value) in extensions ?? new Dictionary<string, object?>())
            problem.Extensions[key] = value;

        http.Response.StatusCode = status;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem });
    }

    /// <summary>An error response from an endpoint handler, e.g. <c>404 notFound</c>.</summary>
    public static Task SendProblemAsync(
        this IEndpoint endpoint, int status, CancellationToken ct, string? detail = null, string? issueCode = null)
    {
        ct.ThrowIfCancellationRequested();
        return WriteAsync(endpoint.HttpContext, status, issueCode, detail);
    }

    public static Task SendNotFoundProblemAsync(this IEndpoint endpoint, CancellationToken ct) =>
        endpoint.SendProblemAsync(StatusCodes.Status404NotFound, ct);

    /// <summary>A 409 naming the conflict, carrying the conflicting resource in its read shape under
    /// <c>current</c> so the client can resynchronise without a second read.</summary>
    public static Task SendConflictAsync(
        this IEndpoint endpoint, string issueCode, string detail, object? current, CancellationToken ct,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        ct.ThrowIfCancellationRequested();
        return WriteAsync(endpoint.HttpContext, StatusCodes.Status409Conflict, issueCode, detail, current, extensions);
    }
}
