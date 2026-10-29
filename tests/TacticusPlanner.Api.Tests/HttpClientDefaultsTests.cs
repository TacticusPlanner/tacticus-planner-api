using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using TacticusPlanner.Api.Features.V1Import;
using TacticusPlanner.ServiceDefaults;

namespace TacticusPlanner.Api.Tests;

public sealed class HttpClientDefaultsTests
{
    private const string ProbeClientName = "probe";

    [Theory]
    [InlineData("GET", true)]
    [InlineData("POST", false)]
    public async Task StandardResilienceRetriesOnlySafeMethods(string method, bool retried)
    {
        var upstream = new CountingHandler(HttpStatusCode.ServiceUnavailable);
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient(ProbeClientName).ConfigurePrimaryHttpMessageHandler(() => upstream);
        builder.Services.PostConfigureAll<HttpStandardResilienceOptions>(options => options.Retry.Delay = TimeSpan.Zero);
        using var host = builder.Build();
        var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ProbeClientName);

        using var request = new HttpRequestMessage(new HttpMethod(method), "https://upstream.example.test/resource");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        if (retried)
        {
            Assert.True(upstream.Calls > 1);
        }
        else
        {
            Assert.Equal(1, upstream.Calls);
        }
    }

    [Fact]
    public void V1ClientBoundsTheResponseBufferAndDoesNotFollowRedirects()
    {
        using var provider = new ServiceCollection()
            .AddTacticusV1Client("https://v1.example.test", "functions-key")
            .BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TacticusV1Client.HttpClientName);
        Assert.Equal(TacticusV1ClientRegistration.MaxResponseContentBufferSize, client.MaxResponseContentBufferSize);

        HttpMessageHandler? handler = provider.GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(TacticusV1Client.HttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        var primary = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.False(primary.AllowAutoRedirect);
    }

    [Fact]
    public async Task V1ProfileOverTheBufferLimitIsTreatedAsUnreadable()
    {
        var oversized = new string(' ', (int)TacticusV1ClientRegistration.MaxResponseContentBufferSize + 1);
        var services = new ServiceCollection().AddTacticusV1Client("https://v1.example.test", "functions-key");
        services.AddHttpClient(TacticusV1Client.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StaticBodyHandler(oversized));
        using var provider = services.BuildServiceProvider();

        var profile = await new TacticusV1Client(provider.GetRequiredService<IHttpClientFactory>())
            .GetProfileAsync("token", TestContext.Current.CancellationToken);

        Assert.Null(profile);
    }

    private sealed class StaticBodyHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    private sealed class CountingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private int calls;

        public int Calls => calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
