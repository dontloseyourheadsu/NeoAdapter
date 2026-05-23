using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardResponse> GetDashboardAsync(DashboardFilterRequest filter, CancellationToken cancellationToken);
}
