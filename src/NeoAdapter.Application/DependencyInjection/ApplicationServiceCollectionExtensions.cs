using Microsoft.Extensions.DependencyInjection;
using NeoAdapter.Application.Dashboard;
using NeoAdapter.Application.Pipeline;

namespace NeoAdapter.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNeoAdapterApplicationServices()
        {
            services.AddSingleton<IDashboardService, MockDashboardService>();
            services.AddSingleton<IPipelineEditorService, MockPipelineEditorService>();
            return services;
        }
    }
}