using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Auth;

public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    public (string Token, DateTimeOffset ExpiresAtUtc) CreateToken(UserAccount user)
    {
        var issuer = configuration["Jwt:Issuer"] ?? "NeoAdapter";
        var audience = configuration["Jwt:Audience"] ?? "NeoAdapter.Client";
        var key = configuration["Jwt:Key"] ?? "ChangeThisInDevelopmentOnly_AtLeast32Characters";
        var expiresMinutes = int.TryParse(configuration["Jwt:ExpiresMinutes"], out var parsed) ? parsed : 120;

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiresMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username)
        };

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}