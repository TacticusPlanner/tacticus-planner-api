using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TacticusPlanner.Api.Features.UserJot;

namespace TacticusPlanner.Api.Tests;

public sealed class UserJotTraitsTests
{
    [Theory]
    [InlineData("v2.6.0")]
    [InlineData("2.6.0-rc.1+abc123")]
    [InlineData("1a950e1d0f3c4b2a8e7d6c5b4a3f2e1d0c9b8a7f")]
    public void WellFormedAppVersionIsKept(string appVersion)
    {
        var traits = UserJotTraits.Build(appVersion, null, null);

        Assert.Equal(appVersion, traits[UserJotTraits.AppVersion]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-v2")]
    [InlineData("v2.6.0 <script>")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void MalformedAppVersionIsDropped(string appVersion)
    {
        var traits = UserJotTraits.Build(appVersion, null, null);

        Assert.False(traits.ContainsKey(UserJotTraits.AppVersion));
    }

    [Theory]
    [InlineData("installed", true)]
    [InlineData("browser", true)]
    [InlineData("Installed", false)]
    [InlineData("tablet", false)]
    public void OnlyKnownAppModesAreKept(string appMode, bool kept)
    {
        var traits = UserJotTraits.Build(null, appMode, null);

        Assert.Equal(kept, traits.ContainsKey(UserJotTraits.AppMode));
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("pt-BR", true)]
    [InlineData("EN", false)]
    [InlineData("english", false)]
    [InlineData("en-us", false)]
    public void OnlyLanguageTagsAreKept(string language, bool kept)
    {
        var traits = UserJotTraits.Build(null, null, language);

        Assert.Equal(kept, traits.ContainsKey(UserJotTraits.Language));
    }

    [Theory]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36",
        "Chrome 131",
        "Android"
    )]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1",
        "Safari 17",
        "iOS"
    )]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/131.0.6778.73 Mobile/15E148 Safari/604.1",
        "Chrome 131",
        "iOS"
    )]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Safari/605.1.15",
        "Safari 18",
        "macOS"
    )]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0",
        "Edge 131",
        "Windows"
    )]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 OPR/115.0.0.0",
        "Opera 115",
        "Windows"
    )]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:133.0) Gecko/20100101 Firefox/133.0", "Firefox 133", "Linux")]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/26.0 Chrome/122.0.0.0 Mobile Safari/537.36",
        "Samsung Internet 26",
        "Android"
    )]
    [InlineData(
        "Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
        "Chrome 131",
        "ChromeOS"
    )]
    public void UserAgentIsReducedToBrowserAndOsFamilies(string userAgent, string browser, string os)
    {
        var traits = UserJotTraits.Build(null, null, null, userAgent);

        Assert.Equal(browser, traits[UserJotTraits.Browser]);
        Assert.Equal(os, traits[UserJotTraits.OperatingSystem]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curl/8.5.0")]
    public void UnknownUserAgentAddsNoBrowserOrOs(string? userAgent)
    {
        Assert.Empty(UserJotTraits.Build(null, null, null, userAgent));
    }

    [Fact]
    public void NoContextBuildsNoTraits()
    {
        Assert.Empty(UserJotTraits.Build(null, null, null));
    }

    [Fact]
    public void SignedTokenLeavesOutEmptyTraits()
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(
            CreateSigner().CreateToken(Guid.NewGuid(), "Ada", UserJotTraits.Build(null, null, null))
        );

        Assert.DoesNotContain(token.Claims, claim => claim.Type == "traits");
    }

    [Fact]
    public void SignedTokenCarriesTraitsAsAFlatObject()
    {
        var traits = UserJotTraits.Build("v2.6.0", "browser", "en");

        var token = new JwtSecurityTokenHandler().ReadJwtToken(CreateSigner().CreateToken(Guid.NewGuid(), "Ada", traits));

        using var payload = JsonDocument.Parse(token.Payload.SerializeToJson());
        var claim = payload.RootElement.GetProperty("traits");
        Assert.Equal(JsonValueKind.Object, claim.ValueKind);
        Assert.Equal("v2.6.0", claim.GetProperty("appVersion").GetString());
        Assert.Equal("browser", claim.GetProperty("appMode").GetString());
        Assert.Equal("en", claim.GetProperty("language").GetString());
        Assert.All(claim.EnumerateObject(), property =>
            Assert.DoesNotContain(property.Value.ValueKind, new[] { JsonValueKind.Object, JsonValueKind.Array }));
    }

    private static UserJotTokenSigner CreateSigner() =>
        new(
            Options.Create(new UserJotOptions { ProjectId = "project", ProjectSecret = new string('s', 32) }),
            TimeProvider.System
        );
}
