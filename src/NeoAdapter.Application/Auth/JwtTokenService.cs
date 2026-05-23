using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Auth;

public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    public (string Token, DateTimeOffset ExpiresAtUtc) CreateAccessToken(UserAccount user)
    {
        var issuer = configuration["Jwt:Issuer"] ?? "NeoAdapter";
        var audience = configuration["Jwt:Audience"] ?? "NeoAdapter.Client";
        var key = configuration["Jwt:Key"] ?? "ChangeThisInDevelopmentOnly_AtLeast32Characters";
        
        // Access token: 15 minutes as requested
        var expiresMinutes = int.TryParse(configuration["Jwt:AccessTokenExpiresMinutes"], out var parsed) ? parsed : 15;

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiresMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) CreateRefreshToken()
    {
        // Refresh token: 6 months as requested (approx 180 days)
        var expiresDays = int.TryParse(configuration["Jwt:RefreshTokenExpiresDays"], out var parsed) ? parsed : 180;
        
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        
        var token = Convert.ToBase64String(bytes);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(expiresDays);
        
        return (token, expiresAt);
    }
}
