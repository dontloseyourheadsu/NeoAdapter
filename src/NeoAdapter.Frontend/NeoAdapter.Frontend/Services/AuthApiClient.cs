using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using NeoAdapter.Contracts.Auth;

namespace NeoAdapter.Frontend.Services;

public sealed class AuthApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Uri[] FallbackBaseAddresses =
    [
        new("http://localhost:5193/"),
        new("http://127.0.0.1:5193/"),
        new("https://localhost:7277/")
    ];

    public async Task<AuthResponse?> RegisterAsync(RegisterUserRequest request, CancellationToken cancellationToken)
    {
        var response = await PostWithFallbackAsync("api/auth/register", request, cancellationToken);
        if (response is null)
        {
            throw new InvalidOperationException("Unable to reach the API. Verify the API is running and CORS/URL settings are correct.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Registration failed."
                : error.Trim());
        }

        return await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await PostWithFallbackAsync("api/auth/login", request, cancellationToken);
        if (response is null)
        {
            throw new InvalidOperationException("Unable to reach the API. Verify the API is running and CORS/URL settings are correct.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Invalid username or password."
                : error.Trim());
        }

        return await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions, cancellationToken);
    }

    private async Task<HttpResponseMessage?> PostWithFallbackAsync<TRequest>(
        string relativeUrl,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var baseAddress in EnumerateCandidateBaseAddresses())
        {
            if (!tried.Add(baseAddress.AbsoluteUri))
            {
                continue;
            }

            httpClient.BaseAddress = baseAddress;

            try
            {
                return await httpClient.PostAsJsonAsync(relativeUrl, request, cancellationToken);
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        return null;
    }

    private IEnumerable<Uri> EnumerateCandidateBaseAddresses()
    {
        if (httpClient.BaseAddress is not null)
        {
            yield return Normalize(httpClient.BaseAddress);
        }

        foreach (var fallback in FallbackBaseAddresses)
        {
            yield return fallback;
        }
    }

    private static Uri Normalize(Uri uri)
    {
        var absolute = uri.IsAbsoluteUri ? uri : new Uri(uri.ToString(), UriKind.Absolute);
        if (absolute.AbsoluteUri.EndsWith('/'))
        {
            return absolute;
        }

        return new Uri(absolute.AbsoluteUri + "/", UriKind.Absolute);
    }
}