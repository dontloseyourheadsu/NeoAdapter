using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using NeoAdapter.Frontend.BlazorShared.Services;

namespace NeoAdapter.Frontend.WebBlazor.Services;

public sealed class WebTokenStorage(IJSRuntime jsRuntime) : ITokenStorage
{
    private const string TokenKey = "neoadapter_token";
    private const string RefreshKey = "neoadapter_refresh_token";
    private const string ExpiryKey = "neoadapter_expiry";
    private const string AdminKey = "neoadapter_is_admin";

    public async Task SaveAsync(string accessToken, string refreshToken, DateTimeOffset expiresAtUtc, bool isAdmin)
    {
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", TokenKey, accessToken);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", RefreshKey, refreshToken);
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", ExpiryKey, expiresAtUtc.ToString("o"));
        await jsRuntime.InvokeVoidAsync("localStorage.setItem", AdminKey, isAdmin.ToString());
    }

    public async Task<StoredTokens?> LoadAsync()
    {
        var token = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", TokenKey);
        var refresh = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", RefreshKey);
        var expiryStr = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", ExpiryKey);
        var adminStr = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", AdminKey);

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(expiryStr))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(expiryStr, out var expiry))
        {
            bool.TryParse(adminStr, out var isAdmin);
            return new StoredTokens(token, refresh ?? string.Empty, expiry, isAdmin);
        }

        return null;
    }

    public async Task ClearAsync()
    {
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", TokenKey);
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshKey);
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", ExpiryKey);
        await jsRuntime.InvokeVoidAsync("localStorage.removeItem", AdminKey);
    }
}
