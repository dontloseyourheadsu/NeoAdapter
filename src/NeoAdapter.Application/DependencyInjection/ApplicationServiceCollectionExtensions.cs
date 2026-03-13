using Microsoft.Extensions.DependencyInjection;
using NeoAdapter.Application.Dashboard;

namespace NeoAdapter.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNeoAdapterApplicationServices()
        {
            services.AddSingleton<IDashboardService, MockDashboardService>();
            return services;
        }
    }
}