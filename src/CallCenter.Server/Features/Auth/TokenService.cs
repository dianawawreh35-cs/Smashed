using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CallCenter.Server.Data.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CallCenter.Server.Features.Auth;

/// <summary>Issues the bearer tokens used by the Agent App and the supervisor SPA.</summary>
/// <param name="timeProvider">
/// The clock the token is stamped with. Injected so expiry can be tested
/// without waiting out a real lifetime.
/// </param>
public class TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>Builds the signing key from configuration.</summary>
    public static SymmetricSecurityKey CreateSigningKey(string signingKey) =>
        new(Encoding.UTF8.GetBytes(signingKey));

    /// <summary>
    /// Issues a token for <paramref name="user"/>, carrying the session id when
    /// the login opened one, so that logout and presence can find it again.
    /// </summary>
    public (string Token, DateTimeOffset ExpiresAt) Issue(User user, Guid? sessionId)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.Add(_options.Lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Login),
            new(ClaimTypes.Role, user.Role),
        };

        if (sessionId is not null)
        {
            claims.Add(new Claim(AppClaims.SessionId, sessionId.Value.ToString()));
        }

        var credentials = new SigningCredentials(
            CreateSigningKey(_options.SigningKey), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
