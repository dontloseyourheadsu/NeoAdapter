using NeoAdapter.Domain;

namespace NeoAdapter.Application.Auth;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAtUtc) CreateAccessToken(UserAccount user);
    
    (string Token, DateTimeOffset ExpiresAtUtc) CreateRefreshToken();
}
