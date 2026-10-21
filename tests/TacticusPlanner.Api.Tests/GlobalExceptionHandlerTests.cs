using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TacticusPlanner.Api.Http;

namespace TacticusPlanner.Api.Tests;

/// <summary><c>api-error-contract</c>: an unexpected failure is a 500 <c>internal</c> problem without a stack trace,
/// and a response whose headers already started is aborted, never rewritten.</summary>
public sealed class GlobalExceptionHandlerTests
{
    private static (GlobalExceptionHandler Handler, DefaultHttpContext Context, MemoryStream Body) Create()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails(options =>
                options.CustomizeProblemDetails = context => ApiProblems.Complete(context.ProblemDetails, context.HttpContext))
            .BuildServiceProvider();
        var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "trace-1" };
        context.Request.Path = "/api/v1/me/goals";
        context.Request.Headers.Accept = "application/json";
        context.Response.Body = body;
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance, services.GetRequiredService<IProblemDetailsService>());
        return (handler, context, body);
    }

    [Fact]
    public async Task AnUnexpectedFailureIsAnInternalProblemWithoutAStackTrace()
    {
        var (handler, context, body) = Create();

        var handled = await handler.TryHandleAsync(
            context, new InvalidOperationException("secret detail"), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        var text = System.Text.Encoding.UTF8.GetString(body.ToArray());
        var problem = JsonNode.Parse(text)!.AsObject();
        Assert.Equal("internal", problem["issueCode"]!.GetValue<string>());
        Assert.Equal("trace-1", problem["traceId"]!.GetValue<string>());
        Assert.Equal("/api/v1/me/goals", problem["instance"]!.GetValue<string>());
        Assert.DoesNotContain("secret detail", text, StringComparison.Ordinal);
        Assert.DoesNotContain("at TacticusPlanner", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailureAfterTheResponseStartedAbortsWithoutAppendingAProblem()
    {
        var (handler, context, body) = Create();
        var lifetime = new AbortTracking();
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        await body.WriteAsync("[{\"partial\":"u8.ToArray(), TestContext.Current.CancellationToken);

        var handled = await handler.TryHandleAsync(
            context, new InvalidOperationException("mid-stream"), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.True(lifetime.Aborted);
        Assert.Equal("[{\"partial\":", System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    private sealed class StartedResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class AbortTracking : IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }

        public CancellationToken RequestAborted { get; set; }

        public void Abort() => Aborted = true;
    }
}
