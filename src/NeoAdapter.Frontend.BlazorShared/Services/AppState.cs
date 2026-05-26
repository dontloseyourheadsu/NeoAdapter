using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Auth;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.IntegrationJobs;

namespace NeoAdapter.Frontend.BlazorShared.Services;

public sealed class AppState
{
    private readonly HttpClient _httpClient;
    private readonly AuthApiClient _authApiClient;
    private readonly ITokenStorage _tokenStorage;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private bool _isRefreshing;

    public AppState(HttpClient httpClient, AuthApiClient authApiClient, ITokenStorage tokenStorage)
    {
        _httpClient = httpClient;
        _authApiClient = authApiClient;
        _tokenStorage = tokenStorage;
    }

    public event Action? OnStateChanged;

    public bool IsAuthenticated { get; private set; }
    public string CurrentUsername { get; private set; } = string.Empty;
    public bool IsAdmin { get; private set; }
    public string AccessToken { get; private set; } = string.Empty;
    public string RefreshToken { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; } = DateTimeOffset.MinValue;

    public string ActiveView { get; set; } = "dashboard";
    public string? ErrorMessage { get; set; }
    public string? StatusMessage { get; set; }

    public IReadOnlyList<ConnectorDto> Connectors { get; set; } = Array.Empty<ConnectorDto>();
    public IReadOnlyList<IntegrationJobDto> IntegrationJobs { get; set; } = Array.Empty<IntegrationJobDto>();

    public void NotifyStateChanged() => OnStateChanged?.Invoke();

    public async Task InitializeAsync()
    {
        try
        {
            var stored = await _tokenStorage.LoadAsync();
            if (stored is null) return;

            AccessToken = stored.AccessToken;
            RefreshToken = stored.RefreshToken;
            ExpiresAtUtc = stored.ExpiresAtUtc;
            IsAdmin = stored.IsAdmin;

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

            // Verify or refresh session immediately
            await EnsureAuthenticatedAsync();
        }
        catch
        {
            await LogoutAsync();
        }
    }

    public async Task ApplyAuthResponseAsync(AuthResponse response)
    {
        IsAuthenticated = true;
        CurrentUsername = response.Username;
        IsAdmin = response.IsAdmin;
        AccessToken = response.AccessToken;
        ExpiresAtUtc = response.ExpiresAtUtc;
        RefreshToken = response.RefreshToken ?? string.Empty;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        if (!string.IsNullOrEmpty(RefreshToken))
        {
            await _tokenStorage.SaveAsync(AccessToken, RefreshToken, ExpiresAtUtc, IsAdmin);
        }

        ErrorMessage = null;
        StatusMessage = $"Signed in as {response.Username}.";
        NotifyStateChanged();
    }

    public async Task LogoutAsync()
    {
        IsAuthenticated = false;
        CurrentUsername = string.Empty;
        IsAdmin = false;
        AccessToken = string.Empty;
        RefreshToken = string.Empty;
        ExpiresAtUtc = DateTimeOffset.MinValue;

        _httpClient.DefaultRequestHeaders.Authorization = null;
        await _tokenStorage.ClearAsync();

        ActiveView = "dashboard";
        ErrorMessage = null;
        StatusMessage = "Logged out.";
        NotifyStateChanged();
    }

    public async Task<bool> EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrEmpty(RefreshToken))
        {
            return false;
        }

        // If token expires in less than 2 minutes, refresh it
        if (ExpiresAtUtc < DateTimeOffset.UtcNow.AddMinutes(2))
        {
            await _refreshSemaphore.WaitAsync();
            try
            {
                if (_isRefreshing) return IsAuthenticated;
                _isRefreshing = true;

                var response = await _authApiClient.RefreshAsync(new RefreshTokenRequest(RefreshToken), CancellationToken.None);
                if (response is not null)
                {
                    await ApplyAuthResponseAsync(response);
                }
                else
                {
                    await LogoutAsync();
                }
            }
            catch
            {
                await LogoutAsync();
            }
            finally
            {
                _isRefreshing = false;
                _refreshSemaphore.Release();
            }
        }
        else
        {
            IsAuthenticated = true;
        }

        return IsAuthenticated;
    }
}
