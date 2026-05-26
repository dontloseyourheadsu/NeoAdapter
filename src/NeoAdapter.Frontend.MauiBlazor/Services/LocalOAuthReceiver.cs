using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Auth;

namespace NeoAdapter.Frontend.MauiBlazor.Services;

public sealed class LocalOAuthReceiver
{
    public static int GetRandomUnusedPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task<AuthResponse?> ReceiveAuthResponseAsync(int port, CancellationToken cancellationToken)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/callback/");
        listener.Start();

        try
        {
            var contextTask = listener.GetContextAsync();
            using (cancellationToken.Register(() =>
            {
                try { listener.Stop(); } catch { }
            }))
            {
                HttpListenerContext context;
                try
                {
                    context = await contextTask;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                var request = context.Request;
                var response = context.Response;

                var query = request.QueryString;
                var token = query["token"];
                var username = query["username"];
                var isAdminStr = query["isAdmin"];
                var refreshToken = query["refreshToken"];
                var error = query["error"];

                AuthResponse? authResponse = null;

                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(username))
                {
                    bool isAdmin = bool.TryParse(isAdminStr, out var parsedAdmin) && parsedAdmin;
                    var expiresAtStr = query["expiresAtUtc"];
                    DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
                    if (DateTimeOffset.TryParse(expiresAtStr, out var parsedExpires))
                    {
                        expiresAt = parsedExpires;
                    }

                    authResponse = new AuthResponse(
                        Guid.Empty,
                        username,
                        token,
                        expiresAt,
                        refreshToken,
                        IsAdmin: isAdmin
                    );
                }

                string responseString = @"
<!DOCTYPE html>
<html>
<head>
    <title>NeoAdapter Authentication</title>
    <style>
        body {
            background-color: #0A0910;
            color: #E8E3F5;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            margin: 0;
        }
        .container {
            background-color: #141220;
            border: 1px solid #2A2440;
            border-radius: 12px;
            padding: 30px;
            text-align: center;
            max-width: 400px;
            box-shadow: 0 4px 20px rgba(0,0,0,0.5);
        }
        h1 {
            color: #C7A6FF;
            margin-top: 0;
        }
        p {
            color: #9B93B8;
            line-height: 1.6;
        }
        .success-icon {
            font-size: 48px;
            color: #C7A6FF;
            margin-bottom: 15px;
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='success-icon'>✓</div>
        <h1>Authentication Successful</h1>
        <p>You have successfully logged in to NeoAdapter.</p>
        <p>You can now close this browser tab and return to the application.</p>
    </div>
</body>
</html>";

                if (!string.IsNullOrEmpty(error))
                {
                    responseString = responseString.Replace("Authentication Successful", "Authentication Failed")
                                                   .Replace("✓", "✗")
                                                   .Replace("color: #C7A6FF;", "color: #FF5F7E;")
                                                   .Replace("color: #C7A6FF", "color: #FF5F7E")
                                                   .Replace("successfully logged in to NeoAdapter", $"failed to log in to NeoAdapter. Error: {WebUtility.HtmlEncode(error)}");
                }

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                using (var output = response.OutputStream)
                {
                    await output.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
                }

                return authResponse;
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            try { listener.Close(); } catch { }
        }
    }
}
