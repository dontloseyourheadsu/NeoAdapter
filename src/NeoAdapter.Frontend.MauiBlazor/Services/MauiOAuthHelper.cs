using System;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Auth;
using NeoAdapter.Frontend.BlazorShared.Services;

namespace NeoAdapter.Frontend.MauiBlazor.Services;

public sealed class MauiOAuthHelper : IOAuthHelper
{
    public async Task<AuthResponse?> SignInAsync(string apiBase, CancellationToken cancellationToken)
    {
        int port = LocalOAuthReceiver.GetRandomUnusedPort();
        var receiver = new LocalOAuthReceiver();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);
        
        var receiverTask = receiver.ReceiveAuthResponseAsync(port, linkedCts.Token);

        var loginUrl = $"{apiBase.TrimEnd('/')}/api/auth/google/login?port={port}";
        
        try
        {
#if WINDOWS
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = loginUrl,
                UseShellExecute = true
            });
#else
            await Microsoft.Maui.ApplicationModel.Browser.Default.OpenAsync(loginUrl, Microsoft.Maui.ApplicationModel.BrowserLaunchMode.External);
#endif
        }
        catch
        {
            // Fallback: try standard Process.Start
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = loginUrl,
                    UseShellExecute = true
                });
            }
            catch {}
        }

        return await receiverTask;
    }
}
