using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Application.Security;
using NeoAdapter.Contracts.Auth;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Auth;

public sealed class AuthService(
    NeoAdapterDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            throw new InvalidOperationException("Password must be at least 8 characters long.");
        }

        var exists = await dbContext.UserAccounts
            .AnyAsync(user => user.Username.ToLower() == username.ToLower(), cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("Username already exists.");
        }

        var (hash, salt) = passwordHasher.HashPassword(request.Password);
        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastLoginAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.UserAccounts.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var token = jwtTokenService.CreateToken(user);
        return new AuthResponse(user.Id, user.Username, token.Token, token.ExpiresAtUtc);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(item => item.Username.ToLower() == username.ToLower(), cancellationToken)
            ?? throw new InvalidOperationException("Invalid username or password.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            throw new InvalidOperationException("Invalid username or password.");
        }

        user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var token = jwtTokenService.CreateToken(user);
        return new AuthResponse(user.Id, user.Username, token.Token, token.ExpiresAtUtc);
    }
}