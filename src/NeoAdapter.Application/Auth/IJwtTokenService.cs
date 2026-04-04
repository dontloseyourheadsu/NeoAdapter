using NeoAdapter.Domain;

namespace NeoAdapter.Application.Auth;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAtUtc) CreateToken(UserAccount user);
}