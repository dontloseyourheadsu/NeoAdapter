using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NeoAdapter.Application.Auth;
using NeoAdapter.Contracts.Auth;

namespace NeoAdapter.Api.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAuthService authService,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpGet("ping")]
    public ActionResult<object> Ping()
    {
        return Ok(new
        {
            status = "ok",
            controller = "auth",
            timestampUtc = DateTimeOffset.UtcNow
        });
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.RegisterAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.LoginAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(ex.Message);
        }
        }

        [HttpPost("refresh")]
        public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
        {
            try
            {
                var response = await authService.RefreshTokenAsync(request, cancellationToken);
                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                return Unauthorized(ex.Message);
            }
        }

        [HttpGet("google/login")]
        public IActionResult GoogleLogin([FromQuery] int port)
        {
            var clientId = configuration["Authentication:Google:ClientId"];
            if (string.IsNullOrEmpty(clientId))
            {
                return BadRequest("Google Authentication is not configured on this server. Please check Configuration Authentication:Google:ClientId.");
            }

            var redirectUri = Url.Action(nameof(GoogleCallback), "Auth", null, Request.Scheme) 
                              ?? $"{Request.Scheme}://{Request.Host}/api/auth/google/callback";

            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?" +
                          $"client_id={Uri.EscapeDataString(clientId)}" +
                          $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                          $"&response_type=code" +
                          $"&scope=openid%20email%20profile" +
                          $"&state={port}" +
                          $"&prompt=select_account";

            return Redirect(authUrl);
        }

        [HttpGet("google/callback")]
        public async Task<IActionResult> GoogleCallback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            CancellationToken cancellationToken)
        {
            var port = state;
            if (string.IsNullOrEmpty(port) || !int.TryParse(port, out _))
            {
                port = "5055"; // default fallback port
            }

            if (!string.IsNullOrEmpty(error))
            {
                return Redirect($"http://127.0.0.1:{port}/callback?error={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrEmpty(code))
            {
                return Redirect($"http://127.0.0.1:{port}/callback?error=missing_code");
            }

            try
            {
                var clientId = configuration["Authentication:Google:ClientId"];
                var clientSecret = configuration["Authentication:Google:ClientSecret"];
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    return Redirect($"http://127.0.0.1:{port}/callback?error=google_auth_not_configured");
                }

                var redirectUri = Url.Action(nameof(GoogleCallback), "Auth", null, Request.Scheme) 
                                  ?? $"{Request.Scheme}://{Request.Host}/api/auth/google/callback";

                using var client = httpClientFactory.CreateClient();
                var tokenRequestParams = new Dictionary<string, string>
                {
                    { "client_id", clientId },
                    { "client_secret", clientSecret },
                    { "code", code },
                    { "grant_type", "authorization_code" },
                    { "redirect_uri", redirectUri }
                };

                var tokenResponse = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(tokenRequestParams), cancellationToken);
                if (!tokenResponse.IsSuccessStatusCode)
                {
                    var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                    return Redirect($"http://127.0.0.1:{port}/callback?error={Uri.EscapeDataString("Failed to exchange code: " + errorContent)}");
                }

                var tokenData = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken);
                if (tokenData == null || string.IsNullOrEmpty(tokenData.access_token))
                {
                    return Redirect($"http://127.0.0.1:{port}/callback?error=invalid_token_response");
                }

                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.access_token);
                var userInfoResponse = await client.GetAsync("https://www.googleapis.com/oauth2/v3/userinfo", cancellationToken);
                if (!userInfoResponse.IsSuccessStatusCode)
                {
                    return Redirect($"http://127.0.0.1:{port}/callback?error=failed_to_get_user_info");
                }

                var googleUser = await userInfoResponse.Content.ReadFromJsonAsync<GoogleUserInfo>(cancellationToken);
                if (googleUser == null || string.IsNullOrEmpty(googleUser.email))
                {
                    return Redirect($"http://127.0.0.1:{port}/callback?error=invalid_user_info");
                }

                var authResponse = await authService.ProcessGoogleUserAsync(googleUser, cancellationToken);

                var expiresAtUtcString = authResponse.ExpiresAtUtc.ToString("o");
                var redirectUrl = $"http://127.0.0.1:{port}/callback?" +
                                  $"token={Uri.EscapeDataString(authResponse.AccessToken)}" +
                                  $"&username={Uri.EscapeDataString(authResponse.Username)}" +
                                  $"&isAdmin={authResponse.IsAdmin.ToString().ToLower()}" +
                                  $"&expiresAtUtc={Uri.EscapeDataString(expiresAtUtcString)}";

                if (!string.IsNullOrEmpty(authResponse.RefreshToken))
                {
                    redirectUrl += $"&refreshToken={Uri.EscapeDataString(authResponse.RefreshToken)}";
                }

                return Redirect(redirectUrl);
            }
            catch (Exception ex)
            {
                return Redirect($"http://127.0.0.1:{port}/callback?error={Uri.EscapeDataString(ex.Message)}");
            }
        }
    }

file record GoogleTokenResponse(
    string access_token,
    string expires_in,
    string scope,
    string token_type,
    string id_token);