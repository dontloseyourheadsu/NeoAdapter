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
using NeoAdapter.Contracts.Pipeline;
using NeoAdapter.Frontend.Models;
using NeoAdapter.Frontend.Services;

namespace NeoAdapter.Frontend.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly DashboardApiClient _dashboardApiClient;
    private readonly PipelineEditorApiClient _pipelineEditorApiClient;
    private readonly PeriodicTimer _refreshTimer = new(TimeSpan.FromSeconds(6));
    private readonly CancellationTokenSource _refreshCts = new();
    private bool _isRefreshing;
    private bool _isHydratingPipeline;

    public MainViewModel()
        : this(
            new DashboardApiClient(new HttpClient
            {
                BaseAddress = new Uri("http://localhost:5193/")
            }),
            new PipelineEditorApiClient(new HttpClient
            {
                BaseAddress = new Uri("http://localhost:5193/")
            }))
    {
    }

    public MainViewModel(
        DashboardApiClient dashboardApiClient,
        PipelineEditorApiClient pipelineEditorApiClient)
    {
        _dashboardApiClient = dashboardApiClient;
        _pipelineEditorApiClient = pipelineEditorApiClient;
        RefreshCommand = new AsyncRelayCommand(() => RefreshDashboardAsync(_refreshCts.Token));
        SetSourceConnectorCommand = new RelayCommand<ConnectorType>(SetSourceConnector);
        SetDestinationConnectorCommand = new RelayCommand<ConnectorType>(SetDestinationConnector);
        SwapConnectorsCommand = new RelayCommand(SwapConnectors);

        PipelineDraft = new JobPipelineDraft
        {
            SourceConnectorType = ConnectorType.Sql,
            DestinationConnectorType = ConnectorType.Csv,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        UpdatePipelineStatus();

        _ = StartPollingAsync();
    }

    public ObservableCollection<DashboardScheduleItem> Schedule { get; } = [];

    public ObservableCollection<DashboardJobItem> ActiveJobsList { get; } = [];

    public ObservableCollection<DashboardIntegrationItem> Integrations { get; } = [];

    public ObservableCollection<DashboardLogItem> Logs { get; } = [];

    public ObservableCollection<ConnectorOption> ConnectorOptions { get; } =
    [
        new(ConnectorType.Sql, "SQL", "Relational database connector", "DB"),
        new(ConnectorType.Csv, "CSV", "Delimited flat-file connector", "CSV")
    ];

    public JobPipelineDraft PipelineDraft { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand<ConnectorType> SetSourceConnectorCommand { get; }

    public IRelayCommand<ConnectorType> SetDestinationConnectorCommand { get; }

    public IRelayCommand SwapConnectorsCommand { get; }

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

    [ObservableProperty]
    private ConnectorType _selectedSourceConnectorType = ConnectorType.Sql;

    [ObservableProperty]
    private ConnectorType _selectedDestinationConnectorType = ConnectorType.Csv;

    [ObservableProperty]
    private string _pipelineStatus = "Draft pipeline";

    public string SourceConnectorLabel => SelectedSourceConnectorType.ToString().ToUpperInvariant();

    public string DestinationConnectorLabel => SelectedDestinationConnectorType.ToString().ToUpperInvariant();

    public void Dispose()
    {
        _refreshCts.Cancel();
        _refreshTimer.Dispose();
        _refreshCts.Dispose();
    }

    private async Task StartPollingAsync()
    {
        await LoadPipelineEditorAsync(_refreshCts.Token);
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

    private async Task LoadPipelineEditorAsync(CancellationToken cancellationToken)
    {
        try
        {
            var editor = await _pipelineEditorApiClient.GetEditorAsync(cancellationToken);
            if (editor is null)
            {
                return;
            }

            _isHydratingPipeline = true;

            var connectorOptions = editor.Connectors
                .Select(connector => new ConnectorOption(
                    connector.Type,
                    connector.DisplayName,
                    connector.Description,
                    connector.BadgeText));

            ReplaceAll(ConnectorOptions, connectorOptions);

            SelectedSourceConnectorType = editor.Draft.SourceConnectorType;
            SelectedDestinationConnectorType = editor.Draft.DestinationConnectorType;
            PipelineStatus = editor.Draft.Status;
            PipelineDraft.UpdatedAtUtc = editor.Draft.UpdatedAtUtc;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Keep local defaults when pipeline endpoint is unavailable.
        }
        finally
        {
            _isHydratingPipeline = false;
        }
    }

    partial void OnSelectedSourceConnectorTypeChanged(ConnectorType value)
    {
        PipelineDraft.SourceConnectorType = value;
        PipelineDraft.UpdatedAtUtc = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(SourceConnectorLabel));
        UpdatePipelineStatus();

        if (!_isHydratingPipeline)
        {
            _ = PersistPipelineDraftAsync(_refreshCts.Token);
        }
    }

    partial void OnSelectedDestinationConnectorTypeChanged(ConnectorType value)
    {
        PipelineDraft.DestinationConnectorType = value;
        PipelineDraft.UpdatedAtUtc = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(DestinationConnectorLabel));
        UpdatePipelineStatus();

        if (!_isHydratingPipeline)
        {
            _ = PersistPipelineDraftAsync(_refreshCts.Token);
        }
    }

    private void SetSourceConnector(ConnectorType connectorType)
    {
        SelectedSourceConnectorType = connectorType;
    }

    private void SetDestinationConnector(ConnectorType connectorType)
    {
        SelectedDestinationConnectorType = connectorType;
    }

    private void SwapConnectors()
    {
        (SelectedSourceConnectorType, SelectedDestinationConnectorType) =
            (SelectedDestinationConnectorType, SelectedSourceConnectorType);
    }

    private void UpdatePipelineStatus()
    {
        PipelineStatus =
            $"{SourceConnectorLabel} -> {DestinationConnectorLabel} ready";
    }

    private async Task PersistPipelineDraftAsync(CancellationToken cancellationToken)
    {
        try
        {
            var updatedDraft = await _pipelineEditorApiClient.UpdateDraftAsync(
                new UpdatePipelineDraftRequest(
                    SelectedSourceConnectorType,
                    SelectedDestinationConnectorType),
                cancellationToken);

            if (updatedDraft is null)
            {
                return;
            }

            PipelineStatus = updatedDraft.Status;
            PipelineDraft.UpdatedAtUtc = updatedDraft.UpdatedAtUtc;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Keep local state when the update endpoint is unavailable.
        }
    }
}
