using NeoAdapter.Contracts.Auth;

namespace NeoAdapter.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
}