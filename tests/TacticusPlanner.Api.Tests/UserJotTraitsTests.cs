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
        var traits = UserJotTraits.Build(appVersion, null, null, tacticusApiLinked: false);

        Assert.Equal(appVersion, traits[UserJotTraits.AppVersion]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-v2")]
    [InlineData("v2.6.0 <script>")]
    [InlineData("12345678901234567890123456789012345678901")]
    public void MalformedAppVersionIsDropped(string appVersion)
    {
        var traits = UserJotTraits.Build(appVersion, null, null, tacticusApiLinked: false);

        Assert.False(traits.ContainsKey(UserJotTraits.AppVersion));
    }

    [Theory]
    [InlineData("installed", true)]
    [InlineData("browser", true)]
    [InlineData("Installed", false)]
    [InlineData("tablet", false)]
    public void OnlyKnownAppModesAreKept(string appMode, bool kept)
    {
        var traits = UserJotTraits.Build(null, appMode, null, tacticusApiLinked: false);

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
        var traits = UserJotTraits.Build(null, null, language, tacticusApiLinked: false);

        Assert.Equal(kept, traits.ContainsKey(UserJotTraits.Language));
    }

    [Fact]
    public void TacticusApiLinkedIsAlwaysPresent()
    {
        Assert.Equal(true, UserJotTraits.Build(null, null, null, tacticusApiLinked: true)[UserJotTraits.TacticusApiLinked]);
        Assert.Equal(false, UserJotTraits.Build(null, null, null, tacticusApiLinked: false)[UserJotTraits.TacticusApiLinked]);
    }

    [Fact]
    public void SignedTokenCarriesTraitsAsAFlatObject()
    {
        var signer = new UserJotTokenSigner(
            Options.Create(new UserJotOptions { ProjectId = "project", ProjectSecret = new string('s', 32) }),
            TimeProvider.System
        );
        var traits = UserJotTraits.Build("v2.6.0", "browser", "en", tacticusApiLinked: true);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(signer.CreateToken(Guid.NewGuid(), "Ada", traits));

        using var payload = JsonDocument.Parse(token.Payload.SerializeToJson());
        var claim = payload.RootElement.GetProperty("traits");
        Assert.Equal(JsonValueKind.Object, claim.ValueKind);
        Assert.Equal("v2.6.0", claim.GetProperty("appVersion").GetString());
        Assert.Equal("browser", claim.GetProperty("appMode").GetString());
        Assert.Equal("en", claim.GetProperty("language").GetString());
        Assert.True(claim.GetProperty("tacticusApiLinked").GetBoolean());
        Assert.All(claim.EnumerateObject(), property =>
            Assert.DoesNotContain(property.Value.ValueKind, new[] { JsonValueKind.Object, JsonValueKind.Array }));
    }
}
