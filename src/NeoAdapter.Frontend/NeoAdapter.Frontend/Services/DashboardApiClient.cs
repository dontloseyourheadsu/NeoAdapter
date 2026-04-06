using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Frontend.Services;

public sealed class DashboardApiClient(HttpClient httpClient)
{
    public async Task<DashboardResponse?> GetDashboardAsync(CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync("api/dashboard", NeoAdapterJsonTypeInfo.For<DashboardResponse>(), cancellationToken);
    }
}