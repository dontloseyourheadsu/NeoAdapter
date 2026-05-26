using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Frontend.BlazorShared.Services;

public sealed class DashboardApiClient(HttpClient httpClient)
{
    public async Task<DashboardResponse?> GetDashboardAsync(DashboardFilterRequest filter, CancellationToken cancellationToken)
    {
        var queryParams = new List<string>();
        if (filter.OrganizationId.HasValue) queryParams.Add($"organizationId={filter.OrganizationId}");
        if (filter.GroupId.HasValue) queryParams.Add($"groupId={filter.GroupId}");
        if (filter.UserId.HasValue) queryParams.Add($"userId={filter.UserId}");

        var url = "api/dashboard";
        if (queryParams.Count > 0)
        {
            url += "?" + string.Join("&", queryParams);
        }

        try
        {
            return await httpClient.GetFromJsonAsync<DashboardResponse>(url, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
