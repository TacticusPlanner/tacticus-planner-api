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
