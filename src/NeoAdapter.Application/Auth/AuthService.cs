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
        var username = request.Username?.Trim();
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
        
        var defaultOrg = await dbContext.Organizations.FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("No organizations found in the system. Registration is unavailable.");

        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt,
            OrganizationId = defaultOrg.Id,
            Role = "User",
            RoleRead = true,
            RoleEdit = true,
            RoleCreate = true,
            RoleAdmin = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastLoginAtUtc = DateTimeOffset.UtcNow
        };

        dbContext.UserAccounts.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = jwtTokenService.CreateAccessToken(user);
        return new AuthResponse(user.Id, user.Username, accessToken.Token, accessToken.ExpiresAtUtc, IsAdmin: user.RoleAdmin);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new InvalidOperationException("Username and password are required.");
        }

        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(item => item.Username.ToLower() == username.ToLower(), cancellationToken)
            ?? throw new InvalidOperationException("Invalid username or password.");

        if (!passwordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            throw new InvalidOperationException("Invalid username or password.");
        }

        user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        
        var accessToken = jwtTokenService.CreateAccessToken(user);
        string? refreshTokenValue = null;

        if (request.RememberMe)
        {
            var refreshToken = jwtTokenService.CreateRefreshToken();
            refreshTokenValue = refreshToken.Token;

            dbContext.UserRefreshTokens.Add(new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Token = refreshTokenValue,
                ExpiresAtUtc = refreshToken.ExpiresAtUtc,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                IsRevoked = false,
                IsUsed = false
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(user.Id, user.Username, accessToken.Token, accessToken.ExpiresAtUtc, refreshTokenValue, IsAdmin: user.RoleAdmin);
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var storedToken = await dbContext.UserRefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken, cancellationToken);

        if (storedToken == null || storedToken.IsRevoked || storedToken.IsUsed || storedToken.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Invalid or expired refresh token.");
        }

        if (storedToken.User == null)
        {
            throw new InvalidOperationException("User not found.");
        }

        // Mark as used
        storedToken.IsUsed = true;
        
        // Generate new access token
        var accessToken = jwtTokenService.CreateAccessToken(storedToken.User);
        
        // Generate new refresh token (rotation)
        var newRefreshToken = jwtTokenService.CreateRefreshToken();
        
        dbContext.UserRefreshTokens.Add(new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = storedToken.UserId,
            Token = newRefreshToken.Token,
            ExpiresAtUtc = newRefreshToken.ExpiresAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsRevoked = false,
            IsUsed = false
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            storedToken.User.Id, 
            storedToken.User.Username, 
            accessToken.Token, 
            accessToken.ExpiresAtUtc, 
            newRefreshToken.Token,
            IsAdmin: storedToken.User.RoleAdmin);
    }

    public async Task<AuthResponse> ProcessGoogleUserAsync(GoogleUserInfo googleUser, CancellationToken cancellationToken)
    {
        var email = googleUser.email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email is required from Google account.");
        }

        // Find user by GoogleId or Email
        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.GoogleId == googleUser.sub || (u.Email != null && u.Email.ToLower() == email.ToLower()) || u.Username.ToLower() == email.ToLower(), cancellationToken);

        if (user != null)
        {
            // Link Google account or update missing fields
            if (string.IsNullOrEmpty(user.GoogleId))
            {
                user.GoogleId = googleUser.sub;
            }
            if (string.IsNullOrEmpty(user.Email))
            {
                user.Email = email;
            }
            user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            // Auto-provision a new user in the default organization
            var defaultOrg = await dbContext.Organizations.FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("No organizations found in the system. Google sign-in is unavailable.");

            // Create a unique username based on email (max 80 chars)
            var username = email;
            if (username.Length > 80)
            {
                username = username.Substring(0, 80);
            }
            var originalUsername = username;
            int counter = 1;
            while (await dbContext.UserAccounts.AnyAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken))
            {
                var suffix = counter.ToString();
                var limit = 80 - suffix.Length;
                username = (originalUsername.Length > limit ? originalUsername.Substring(0, limit) : originalUsername) + suffix;
                counter++;
            }

            user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = email,
                GoogleId = googleUser.sub,
                PasswordHash = "EXTERNAL_GOOGLE_LOGIN",
                PasswordSalt = string.Empty,
                OrganizationId = defaultOrg.Id,
                Role = "User",
                RoleRead = true,
                RoleEdit = true,
                RoleCreate = true,
                RoleAdmin = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastLoginAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.UserAccounts.Add(user);
        }

        var accessToken = jwtTokenService.CreateAccessToken(user);
        var newRefreshToken = jwtTokenService.CreateRefreshToken();

        dbContext.UserRefreshTokens.Add(new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = newRefreshToken.Token,
            ExpiresAtUtc = newRefreshToken.ExpiresAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsRevoked = false,
            IsUsed = false
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            user.Id,
            user.Username,
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            newRefreshToken.Token,
            IsAdmin: user.RoleAdmin);
    }

    public async Task<AuthResponse> ProcessMicrosoftUserAsync(MicrosoftUserInfo microsoftUser, CancellationToken cancellationToken)
    {
        var email = (microsoftUser.mail ?? microsoftUser.userPrincipalName)?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("Email is required from Microsoft account.");
        }

        // Find user by MicrosoftId or Email or Username
        var user = await dbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.MicrosoftId == microsoftUser.id || (u.Email != null && u.Email.ToLower() == email.ToLower()) || u.Username.ToLower() == email.ToLower(), cancellationToken);

        if (user != null)
        {
            // Link Microsoft account or update missing fields
            if (string.IsNullOrEmpty(user.MicrosoftId))
            {
                user.MicrosoftId = microsoftUser.id;
            }
            if (string.IsNullOrEmpty(user.Email))
            {
                user.Email = email;
            }
            user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            // Auto-provision a new user in the default organization
            var defaultOrg = await dbContext.Organizations.FirstOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("No organizations found in the system. Microsoft sign-in is unavailable.");

            // Create a unique username based on email (max 80 chars)
            var username = email;
            if (username.Length > 80)
            {
                username = username.Substring(0, 80);
            }
            var originalUsername = username;
            int counter = 1;
            while (await dbContext.UserAccounts.AnyAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken))
            {
                var suffix = counter.ToString();
                var limit = 80 - suffix.Length;
                username = (originalUsername.Length > limit ? originalUsername.Substring(0, limit) : originalUsername) + suffix;
                counter++;
            }

            user = new UserAccount
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = email,
                MicrosoftId = microsoftUser.id,
                PasswordHash = "EXTERNAL_MICROSOFT_LOGIN",
                PasswordSalt = string.Empty,
                OrganizationId = defaultOrg.Id,
                Role = "User",
                RoleRead = true,
                RoleEdit = true,
                RoleCreate = true,
                RoleAdmin = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                LastLoginAtUtc = DateTimeOffset.UtcNow
            };

            dbContext.UserAccounts.Add(user);
        }

        var accessToken = jwtTokenService.CreateAccessToken(user);
        var newRefreshToken = jwtTokenService.CreateRefreshToken();

        dbContext.UserRefreshTokens.Add(new UserRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = newRefreshToken.Token,
            ExpiresAtUtc = newRefreshToken.ExpiresAtUtc,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsRevoked = false,
            IsUsed = false
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            user.Id,
            user.Username,
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            newRefreshToken.Token,
            IsAdmin: user.RoleAdmin);
    }
}
