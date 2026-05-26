using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components.WebView.Maui;
using NeoAdapter.Frontend.BlazorShared.Services;
using NeoAdapter.Frontend.MauiBlazor.Services;

namespace NeoAdapter.Frontend.MauiBlazor;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
#endif

		// Register HttpClient pointing to the API default
		builder.Services.AddScoped(sp => new HttpClient 
		{ 
			BaseAddress = new Uri("http://localhost:5193/") 
		});

		// Register shared API clients
		builder.Services.AddScoped<AuthApiClient>();
		builder.Services.AddScoped<ConnectorApiClient>();
		builder.Services.AddScoped<DashboardApiClient>();
		builder.Services.AddScoped<IntegrationJobsApiClient>();
		builder.Services.AddScoped<OrgAdminApiClient>();

		// Register state and token storage
		builder.Services.AddSingleton<AppState>();
		builder.Services.AddScoped<ITokenStorage, MauiTokenStorage>();
		builder.Services.AddScoped<IOAuthHelper, MauiOAuthHelper>();

		return builder.Build();
	}
}
