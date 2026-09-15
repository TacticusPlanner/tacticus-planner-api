using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace TacticusPlanner.Api.Features.UserJot;

/// <summary>
/// Signs short-lived identity tokens for UserJot's widget SDK (see
/// https://userjot.com/docs/widget-v3-signed-identity). The signing secret never leaves this class —
/// only the resulting compact JWT is returned to the caller.
/// </summary>
public sealed class UserJotTokenSigner(IOptions<UserJotOptions> options, TimeProvider timeProvider)
{
    private const string Audience = "userjot";

    public string CreateToken(Guid accountId, string displayName)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var payload = new JwtPayload
        {
            { "sub", accountId.ToString() },
            { "iss", options.Value.ProjectId },
            { "aud", Audience },
            { "iat", issuedAt.ToUnixTimeSeconds() },
            { "exp", issuedAt.AddHours(1).ToUnixTimeSeconds() },
            // UserJot has no generic "display name" claim, only firstName/lastName - the planner's
            // display name is a single free-text field, so it goes in firstName rather than being
            // split on whitespace into a firstName/lastName guess.
            { "firstName", displayName },
            // Not part of UserJot's claim contract; guarantees two tokens minted within the same second
            // are still distinct, per the "fresh token per call" requirement.
            { "jti", Guid.NewGuid().ToString() },
        };

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.ProjectSecret)),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(new JwtHeader(signingCredentials), payload);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
