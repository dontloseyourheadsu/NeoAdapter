using Microsoft.Extensions.DependencyInjection;
using NeoAdapter.Application.Auth;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Application.Dashboard;
using NeoAdapter.Application.IntegrationJobs;
using NeoAdapter.Application.Security;
using NeoAdapter.Application.SqlEditor;

namespace NeoAdapter.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddNeoAdapterApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IConnectorService, ConnectorService>();
        services.AddScoped<ISharePointApiClient, SharePointApiClient>();
        services.AddScoped<IOutlookCalendarApiClient, OutlookCalendarApiClient>();
        services.AddScoped<ISqlEditorService, SqlEditorService>();
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
