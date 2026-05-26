using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Auth;

namespace NeoAdapter.Frontend.BlazorShared.Services;

public interface IOAuthHelper
{
    Task<AuthResponse?> SignInAsync(string apiBase, CancellationToken cancellationToken);
}
