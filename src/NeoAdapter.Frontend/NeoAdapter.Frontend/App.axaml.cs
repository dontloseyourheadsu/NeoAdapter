using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using NeoAdapter.Frontend.Services;
using NeoAdapter.Frontend.ViewModels;
using NeoAdapter.Frontend.Views;

namespace NeoAdapter.Frontend;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
                // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
                DisableAvaloniaDataAnnotationValidation();

                _mainViewModel = BuildMainViewModel();

                desktop.MainWindow = new MainWindow
                {
                    DataContext = _mainViewModel
                };

                desktop.Exit += (_, _) => _mainViewModel.Dispose();
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                _mainViewModel = BuildMainViewModel();

                singleViewPlatform.MainView = new MainView
                {
                    DataContext = _mainViewModel
                };
            }
        }
        catch (Exception ex)
        {
            if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new TextBlock
                {
                    Text = $"Startup failed: {ex.Message}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(12)
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private static MainViewModel BuildMainViewModel()
    {
        var baseUrl = Environment.GetEnvironmentVariable("NEOADAPTER_API_BASE_URL");

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (OperatingSystem.IsBrowser())
            {
                // Browser client talks to nginx host; nginx proxies /api to backend.
                baseUrl = "http://localhost:5235/";
            }
            else
            {
                baseUrl = "http://localhost:5193/";
            }
        }

        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var authApiClient = new AuthApiClient(httpClient);
        var dashboardApiClient = new DashboardApiClient(httpClient);
        var connectorApiClient = new ConnectorApiClient(httpClient);
        var integrationJobsApiClient = new IntegrationJobsApiClient(httpClient);
        return new MainViewModel(httpClient, authApiClient, dashboardApiClient, connectorApiClient, integrationJobsApiClient);
    }
}