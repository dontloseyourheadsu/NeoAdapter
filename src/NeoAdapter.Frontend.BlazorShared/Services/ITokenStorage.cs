using System;
using System.Threading.Tasks;

namespace NeoAdapter.Frontend.BlazorShared.Services;

public interface ITokenStorage
{
    Task SaveAsync(string accessToken, string refreshToken, DateTimeOffset expiresAtUtc, bool isAdmin);
    Task<StoredTokens?> LoadAsync();
    Task ClearAsync();
}

public record StoredTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, bool IsAdmin);
