using Microsoft.Extensions.DependencyInjection;
using NeoAdapter.Application.Auth;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Application.Dashboard;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Application.Security;

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
            services.AddScoped<IIntegrationJobScheduler, IntegrationJobScheduler>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddSingleton<IJwtTokenService, JwtTokenService>();
            services.AddSingleton<ISqlSecretProtector, SqlSecretProtector>();
            services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
            return services;
        }
    }
}