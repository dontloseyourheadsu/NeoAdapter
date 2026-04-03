using Microsoft.Extensions.DependencyInjection;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Application.Dashboard;
using NeoAdapter.Application.IntegrationJobs;

namespace NeoAdapter.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNeoAdapterApplicationServices()
        {
            services.AddScoped<IDashboardService, DashboardService>();
            services.AddScoped<IConnectorService, ConnectorService>();
            services.AddScoped<IIntegrationJobService, IntegrationJobService>();
            services.AddScoped<IIntegrationJobExecutor, IntegrationJobExecutor>();
            return services;
        }
    }
}