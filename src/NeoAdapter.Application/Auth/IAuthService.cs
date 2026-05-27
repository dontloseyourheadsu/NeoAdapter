using NeoAdapter.Contracts.Auth;

namespace NeoAdapter.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> ProcessGoogleUserAsync(GoogleUserInfo googleUser, CancellationToken cancellationToken);

    Task<AuthResponse> ProcessMicrosoftUserAsync(MicrosoftUserInfo microsoftUser, CancellationToken cancellationToken);
}

public sealed record GoogleUserInfo(
    string sub,
    string name,
    string email,
    bool email_verified,
    string picture);

public sealed record MicrosoftUserInfo(
    string id,
    string displayName,
    string? mail,
    string userPrincipalName);
