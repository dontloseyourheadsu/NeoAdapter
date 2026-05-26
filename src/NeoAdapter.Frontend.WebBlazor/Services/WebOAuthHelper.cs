using System;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Auth;
using NeoAdapter.Frontend.BlazorShared.Services;

namespace NeoAdapter.Frontend.WebBlazor.Services;

public sealed class WebOAuthHelper : IOAuthHelper
{
    public Task<AuthResponse?> SignInAsync(string apiBase, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Local loopback OAuth listener is not supported on WebAssembly.");
    }
}
