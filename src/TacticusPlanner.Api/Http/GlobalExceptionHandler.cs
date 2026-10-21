using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TacticusPlanner.Api.Http;

/// <summary>
/// The last-resort error writer (<c>api-error-contract</c>): an unexpected exception becomes a 500 <c>internal</c>
/// problem (no stack trace), an optimistic-concurrency failure no endpoint handled a 409 <c>concurrency</c>, and a
/// bad request from the framework a 400. Once response headers have started nothing may be rewritten or appended,
/// so the failure is logged with its trace id and the connection aborted.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        var statusCode = GetStatusCode(exception);
        LogUnhandledRequestException(logger, exception, httpContext.TraceIdentifier, statusCode);

        if (httpContext.Response.HasStarted)
        {
            httpContext.Abort();
            return true;
        }

        httpContext.Response.StatusCode = statusCode;
        var problem = new ProblemDetails { Status = statusCode };
        if (statusCode == StatusCodes.Status409Conflict)
            problem.Extensions["issueCode"] = ApiProblems.Concurrency;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    private static int GetStatusCode(Exception exception)
    {
        return exception switch
        {
            BadHttpRequestException badRequest => badRequest.StatusCode,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Unhandled request exception. TraceId: {TraceId}; StatusCode: {StatusCode}"
    )]
    private static partial void LogUnhandledRequestException(
        ILogger logger,
        Exception exception,
        string traceId,
        int statusCode
    );
}
