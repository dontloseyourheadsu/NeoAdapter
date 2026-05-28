using System;
using System.Net.Http;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NeoAdapter.Frontend.BlazorShared.Services;
using NeoAdapter.Frontend.WebBlazor;
using NeoAdapter.Frontend.WebBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register HttpClient pointing to the API default
builder.Services.AddScoped(sp => new HttpClient 
{ 
    BaseAddress = new Uri("http://localhost:5193/") 
});

// Register shared API clients
builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<ConnectorApiClient>();
builder.Services.AddScoped<SqlEditorApiClient>();
builder.Services.AddScoped<DashboardApiClient>();
builder.Services.AddScoped<IntegrationJobsApiClient>();
builder.Services.AddScoped<OrgAdminApiClient>();

// Register state and token storage
builder.Services.AddSingleton<AppState>();
builder.Services.AddScoped<ITokenStorage, WebTokenStorage>();
builder.Services.AddScoped<IOAuthHelper, WebOAuthHelper>();

await builder.Build().RunAsync();
