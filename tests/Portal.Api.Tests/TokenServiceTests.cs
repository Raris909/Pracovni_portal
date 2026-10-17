using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Portal.Api.Auth;
using Portal.Contracts.Auth;

namespace Portal.Api.Tests;

public sealed class TokenServiceTests
{
    [Fact]
    public void TokenContainsUserRoleAndExpirationAndHasValidSignature()
    {
        var options = new JwtOptions
        {
            Issuer = "tests", Audience = "clients", SigningKey = new string('a', 64), LifetimeMinutes = 30
        };
        var user = new UserResponse(Guid.NewGuid(), "test@example.com", "Test", "User", UserRoles.Candidate);
        var before = DateTimeOffset.UtcNow;
        var result = new TokenService(Options.Create(options), TimeProvider.System).Create(user);

        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(result.AccessToken,
            new TokenValidationParameters
            {
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

        Assert.Equal(user.Id.ToString(), principal.FindFirst("sub")?.Value);
        Assert.Equal(user.Role, principal.FindFirst("role")?.Value);
        Assert.InRange(result.ExpiresAt, before.AddMinutes(30), DateTimeOffset.UtcNow.AddMinutes(30));
    }
}
