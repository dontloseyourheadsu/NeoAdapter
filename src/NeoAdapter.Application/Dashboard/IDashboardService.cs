using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Application.Dashboard;

public interface IDashboardService
{
    Task<DashboardResponse> GetDashboardAsync(CancellationToken cancellationToken);
}