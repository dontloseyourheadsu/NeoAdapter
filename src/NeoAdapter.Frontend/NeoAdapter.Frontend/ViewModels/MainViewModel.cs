using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeoAdapter.Contracts.Dashboard;
using NeoAdapter.Frontend.Services;

namespace NeoAdapter.Frontend.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly DashboardApiClient _dashboardApiClient;
    private readonly PeriodicTimer _refreshTimer = new(TimeSpan.FromSeconds(6));
    private readonly CancellationTokenSource _refreshCts = new();
    private bool _isRefreshing;

    public MainViewModel()
        : this(new DashboardApiClient(new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5193/")
        }))
    {
    }

    public MainViewModel(DashboardApiClient dashboardApiClient)
    {
        _dashboardApiClient = dashboardApiClient;
        RefreshCommand = new AsyncRelayCommand(() => RefreshDashboardAsync(_refreshCts.Token));
        _ = StartPollingAsync();
    }

    public ObservableCollection<DashboardScheduleItem> Schedule { get; } = [];

    public ObservableCollection<DashboardJobItem> ActiveJobsList { get; } = [];

    public ObservableCollection<DashboardIntegrationItem> Integrations { get; } = [];

    public ObservableCollection<DashboardLogItem> Logs { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    [ObservableProperty]
    private int _totalJobs;

    [ObservableProperty]
    private int _activeJobs;

    [ObservableProperty]
    private int _queuedJobs;

    [ObservableProperty]
    private int _failedJobs;

    [ObservableProperty]
    private string _lastUpdated = "--:--:--";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    public void Dispose()
    {
        _refreshCts.Cancel();
        _refreshTimer.Dispose();
        _refreshCts.Dispose();
    }

    private async Task StartPollingAsync()
    {
        await RefreshDashboardAsync(_refreshCts.Token);

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(_refreshCts.Token))
            {
                await RefreshDashboardAsync(_refreshCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshDashboardAsync(CancellationToken cancellationToken)
    {
        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        IsLoading = true;

        try
        {
            var dashboard = await _dashboardApiClient.GetDashboardAsync(cancellationToken);
            if (dashboard is null)
            {
                ErrorMessage = "Dashboard returned no data.";
                return;
            }

            TotalJobs = dashboard.TotalJobs;
            ActiveJobs = dashboard.ActiveJobs;
            QueuedJobs = dashboard.QueuedJobs;
            FailedJobs = dashboard.FailedJobs;
            LastUpdated = dashboard.GeneratedAtUtc.ToLocalTime().ToString("HH:mm:ss");

            ReplaceAll(Schedule, dashboard.Schedule);
            ReplaceAll(ActiveJobsList, dashboard.ActiveJobsList);
            ReplaceAll(Integrations, dashboard.Integrations);
            ReplaceAll(Logs, dashboard.Logs.OrderByDescending(log => log.TimestampUtc).Take(20));

            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Unable to refresh dashboard from API.";
        }
        finally
        {
            IsLoading = false;
            _isRefreshing = false;
        }
    }

    private static void ReplaceAll<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
