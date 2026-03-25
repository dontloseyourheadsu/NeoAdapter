using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Net.Http;
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
        var baseUrl = Environment.GetEnvironmentVariable("NEOADAPTER_API_BASE_URL")
            ?? "http://localhost:5193/";

        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        var dashboardApiClient = new DashboardApiClient(httpClient);
        var pipelineEditorApiClient = new PipelineEditorApiClient(httpClient);
        return new MainViewModel(dashboardApiClient, pipelineEditorApiClient);
    }
}