using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TacticusPlanner.Api.IntegrationTests;

/// <summary>
/// Authenticates every request with claims driven by the <see cref="IssuerHeader"/>/<see cref="SubjectHeader"/>
/// request headers (defaulting to a fixed test issuer/subject), so individual tests can simulate distinct signed-in
/// users without colliding on the same Account row. Send <see cref="NoAuthHeader"/> to exercise anonymous access.
/// </summary>
public sealed class PlannerTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "PlannerTest";
    public const string NoAuthHeader = "X-Test-NoAuth";
    public const string IssuerHeader = "X-Test-Issuer";
    public const string SubjectHeader = "X-Test-Subject";
    public const string NameHeader = "X-Test-Name";
    public const string OmitNameHeader = "X-Test-OmitName";
    public const string DefaultIssuer = "https://example.ciamlogin.com/example.onmicrosoft.com/v2.0";
    public const string DefaultSubject = "test-user";
    public const string DefaultName = "Test User";

    public PlannerTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    )
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey(NoAuthHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("iss", GetHeaderOrDefault(IssuerHeader, DefaultIssuer)),
            new("sub", GetHeaderOrDefault(SubjectHeader, DefaultSubject)),
            new("scp", "access_as_user"),
        };
        if (!Request.Headers.ContainsKey(OmitNameHeader))
        {
            claims.Add(new("name", GetHeaderOrDefault(NameHeader, DefaultName)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string GetHeaderOrDefault(string headerName, string defaultValue)
    {
        return Request.Headers.TryGetValue(headerName, out var values)
            && values.Count > 0
            && !string.IsNullOrEmpty(values[0])
            ? values[0]!
            : defaultValue;
    }
}
