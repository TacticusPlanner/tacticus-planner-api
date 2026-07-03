using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TacticusPlanner.Api.Http;

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
        if (httpContext.Response.HasStarted)
        {
            return false;
        }

        var statusCode = GetStatusCode(exception);
        LogUnhandledRequestException(logger, exception, httpContext.TraceIdentifier, statusCode);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = GetTitle(statusCode),
                Detail = GetDetail(statusCode),
                Instance = httpContext.Request.Path,
                Extensions =
                {
                    ["traceId"] = httpContext.TraceIdentifier,
                },
            },
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

    private static string GetTitle(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad request.",
            StatusCodes.Status403Forbidden => "Forbidden.",
            StatusCodes.Status409Conflict => "Conflict.",
            _ => "An unexpected error occurred.",
        };
    }

    private static string GetDetail(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "The request could not be processed.",
            StatusCodes.Status403Forbidden => "The request is not allowed.",
            StatusCodes.Status409Conflict => "The request conflicted with the current resource state.",
            _ => "The request failed. Use the traceId when contacting support.",
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
