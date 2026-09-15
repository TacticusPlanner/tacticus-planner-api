using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TacticusPlanner.Api.Features.UserJot;

namespace TacticusPlanner.Api.Tests;

public sealed class UserJotTokenEndpointTests(PlannerApiFactory factory) : IClassFixture<PlannerApiFactory>
{
    private const string ProjectId = "test-userjot-project";
    private const string ProjectSecret = "test-userjot-project-secret-for-tests";

    [Fact]
    public async Task AuthenticatedUserReceivesSignedTokenWithCoreClaims()
    {
        var subject = NewSubject();
        var client = CreateAuthenticatedClient(subject);
        var applicationUserId = await ProvisionAccountAsync(client);

        var response = await client.GetAsync("/api/v1/me/userjot-token", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var body = await response.Content.ReadFromJsonAsync<UserJotTokenResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(body);

        var token = ValidateAndReadToken(body.Token);
        Assert.Equal(applicationUserId.ToString(), token.Subject);
        Assert.Equal(ProjectId, token.Issuer);
        Assert.Equal("userjot", Assert.Single(token.Audiences));
        Assert.True(token.ValidTo - token.IssuedAt <= TimeSpan.FromHours(1));
        Assert.Equal(PlannerTestAuthenticationHandler.DefaultName, token.Claims.Single(claim => claim.Type == "firstName").Value);
        Assert.DoesNotContain(token.Claims, claim => claim.Type is "email" or "lastName");
    }

    [Fact]
    public async Task TokenReflectsTheAccountsActualDisplayName()
    {
        var client = CreateAuthenticatedClient(NewSubject(), name: "Ada Lovelace");
        await ProvisionAccountAsync(client);

        var token = ValidateAndReadToken(await GetTokenAsync(client));

        Assert.Equal("Ada Lovelace", token.Claims.Single(claim => claim.Type == "firstName").Value);
    }

    [Fact]
    public async Task EachCallIssuesADistinctToken()
    {
        var client = CreateAuthenticatedClient(NewSubject());
        await ProvisionAccountAsync(client);

        var first = await GetTokenAsync(client);
        var second = await GetTokenAsync(client);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task UnauthenticatedRequestIsRejected()
    {
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/userjot-token");
        request.Headers.Add(PlannerTestAuthenticationHandler.NoAuthHeader, "1");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void MissingProjectSecretFailsAtStartup()
    {
        using var misconfiguredFactory = new PlannerApiFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UserJot:ProjectSecret"] = "",
                });
            });
        });

        var exception = Assert.Throws<OptionsValidationException>(() => misconfiguredFactory.CreateClient());
        Assert.Contains("UserJot:ProjectSecret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseNeverContainsTheProjectSecret()
    {
        var client = CreateAuthenticatedClient(NewSubject());
        await ProvisionAccountAsync(client);

        var response = await client.GetAsync("/api/v1/me/userjot-token", TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(ProjectSecret, raw, StringComparison.Ordinal);
    }

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me/userjot-token", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<UserJotTokenResponse>(TestContext.Current.CancellationToken);
        return body!.Token;
    }

    private static async Task<Guid> ProvisionAccountAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<Features.CurrentUser.CurrentUserResponse>(
            TestContext.Current.CancellationToken
        );
        return body!.ApplicationUserId;
    }

    private static JwtSecurityToken ValidateAndReadToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(
            token,
            new TokenValidationParameters
            {
                ValidIssuer = ProjectId,
                ValidAudience = "userjot",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ProjectSecret)),
            },
            out var validatedToken
        );

        return (JwtSecurityToken)validatedToken;
    }

    private HttpClient CreateAuthenticatedClient(string subject, string? name = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.SubjectHeader, subject);

        if (name is not null)
        {
            client.DefaultRequestHeaders.Add(PlannerTestAuthenticationHandler.NameHeader, name);
        }

        return client;
    }

    private static string NewSubject() => $"userjot-token-{Guid.NewGuid()}";
}
