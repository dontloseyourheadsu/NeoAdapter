namespace NeoAdapter.Contracts.Auth;

public sealed record RegisterUserRequest(
    string Username,
    string Password);

public sealed record LoginRequest(
    string Username,
    string Password,
    bool RememberMe = false);

public sealed record RefreshTokenRequest(
    string RefreshToken);

public sealed record AuthResponse(
    Guid UserId,
    string Username,
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string? RefreshToken = null);
