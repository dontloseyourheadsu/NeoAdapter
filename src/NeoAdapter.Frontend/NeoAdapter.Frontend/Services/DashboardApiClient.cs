using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Frontend.Services;

public sealed class DashboardApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<DashboardResponse?> GetDashboardAsync(CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<DashboardResponse>("api/dashboard", JsonOptions, cancellationToken);
    }
}