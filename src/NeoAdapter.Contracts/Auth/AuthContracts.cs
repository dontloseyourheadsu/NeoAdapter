namespace NeoAdapter.Contracts.Auth;

public sealed record RegisterUserRequest(
    string Username,
    string Password);

public sealed record LoginRequest(
    string Username,
    string Password);

public sealed record AuthResponse(
    Guid UserId,
    string Username,
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);