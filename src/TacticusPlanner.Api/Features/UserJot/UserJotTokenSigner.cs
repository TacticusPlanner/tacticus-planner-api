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

    public string CreateToken(Guid accountId, string? email, string? firstName, string? lastName)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var payload = new JwtPayload
        {
            { "sub", accountId.ToString() },
            { "iss", options.Value.ProjectId },
            { "aud", Audience },
            { "iat", issuedAt.ToUnixTimeSeconds() },
            { "exp", issuedAt.AddHours(1).ToUnixTimeSeconds() },
            // Not part of UserJot's claim contract; guarantees two tokens minted within the same second
            // are still distinct, per the "fresh token per call" requirement.
            { "jti", Guid.NewGuid().ToString() },
        };

        if (email is not null)
        {
            payload["email"] = email;
        }

        if (firstName is not null)
        {
            payload["firstName"] = firstName;
        }

        if (lastName is not null)
        {
            payload["lastName"] = lastName;
        }

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.ProjectSecret)),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(new JwtHeader(signingCredentials), payload);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
