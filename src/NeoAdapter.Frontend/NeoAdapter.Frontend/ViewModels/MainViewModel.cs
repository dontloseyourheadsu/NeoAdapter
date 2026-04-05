using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.Dashboard;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Frontend.Services;

namespace NeoAdapter.Frontend.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AuthApiClient _authApiClient;
    private readonly DashboardApiClient _dashboardApiClient;
    private readonly ConnectorApiClient _connectorApiClient;
    private readonly IntegrationJobsApiClient _integrationJobsApiClient;
    private readonly PeriodicTimer _refreshTimer = new(TimeSpan.FromSeconds(8));
    private readonly CancellationTokenSource _refreshCts = new();
    private bool _isRefreshing;

    public MainViewModel()
        : this(
            new HttpClient { BaseAddress = new Uri("http://localhost:5193/") },
            new AuthApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5193/") }),
            new DashboardApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5193/") }),
            new ConnectorApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5193/") }),
            new IntegrationJobsApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5193/") }))
    {
    }

    public MainViewModel(
        HttpClient httpClient,
        AuthApiClient authApiClient,
        DashboardApiClient dashboardApiClient,
        ConnectorApiClient connectorApiClient,
        IntegrationJobsApiClient integrationJobsApiClient)
    {
        _httpClient = httpClient;
        _authApiClient = authApiClient;
        _dashboardApiClient = dashboardApiClient;
        _connectorApiClient = connectorApiClient;
        _integrationJobsApiClient = integrationJobsApiClient;

        LoginCommand = new AsyncRelayCommand(() => LoginAsync(_refreshCts.Token));
        RegisterCommand = new AsyncRelayCommand(() => RegisterAsync(_refreshCts.Token));
        LogoutCommand = new RelayCommand(Logout);
        RefreshCommand = new AsyncRelayCommand(() => RefreshAllAsync(_refreshCts.Token));
        CreateConnectorCommand = new AsyncRelayCommand(() => CreateConnectorAsync(_refreshCts.Token));
        TestConnectorCommand = new AsyncRelayCommand(() => TestConnectorAsync(_refreshCts.Token));
        CreateIntegrationJobCommand = new AsyncRelayCommand(() => CreateIntegrationJobAsync(_refreshCts.Token));
        RunIntegrationJobCommand = new AsyncRelayCommand<IntegrationJobDto>(RunIntegrationJobAsync);

    }

    public ObservableCollection<DashboardJobSummaryItem> DashboardJobs { get; } = [];

    public ObservableCollection<DashboardRunItem> DashboardRuns { get; } = [];

    public ObservableCollection<ConnectorDto> Connectors { get; } = [];

    public ObservableCollection<IntegrationJobDto> IntegrationJobs { get; } = [];

    public IReadOnlyList<ConnectorType> ConnectorTypes { get; } = [ConnectorType.Sql, ConnectorType.Csv];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand LoginCommand { get; }

    public IAsyncRelayCommand RegisterCommand { get; }

    public IRelayCommand LogoutCommand { get; }

    public IAsyncRelayCommand CreateConnectorCommand { get; }

    public IAsyncRelayCommand TestConnectorCommand { get; }

    public IAsyncRelayCommand CreateIntegrationJobCommand { get; }

    public IAsyncRelayCommand<IntegrationJobDto> RunIntegrationJobCommand { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private int _totalJobs;

    [ObservableProperty]
    private int _enabledJobs;

    [ObservableProperty]
    private int _failedRunsLast24Hours;

    [ObservableProperty]
    private int _totalConnectors;

    [ObservableProperty]
    private string _lastUpdated = "--:--:--";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isAuthenticated;

    public bool IsNotAuthenticated => !IsAuthenticated;

    [ObservableProperty]
    private string _loginUsername = string.Empty;

    [ObservableProperty]
    private string _loginPassword = string.Empty;

    [ObservableProperty]
    private string _currentUsername = string.Empty;

    [ObservableProperty]
    private string _newConnectorName = string.Empty;

    [ObservableProperty]
    private ConnectorType _newConnectorType = ConnectorType.Sql;

    [ObservableProperty]
    private string _sqlServer = "localhost";

    [ObservableProperty]
    private int _sqlPort = 1433;

    [ObservableProperty]
    private string _sqlDatabase = string.Empty;

    [ObservableProperty]
    private string _sqlUsername = string.Empty;

    [ObservableProperty]
    private string _sqlPassword = string.Empty;

    [ObservableProperty]
    private string _sqlTable = string.Empty;

    [ObservableProperty]
    private bool _sqlTrustServerCertificate = true;

    [ObservableProperty]
    private string _csvPath = string.Empty;

    [ObservableProperty]
    private string _csvDelimiter = ",";

    [ObservableProperty]
    private string _newIntegrationJobName = string.Empty;

    [ObservableProperty]
    private ConnectorDto? _selectedSourceConnector;

    [ObservableProperty]
    private ConnectorDto? _selectedDestinationConnector;

    [ObservableProperty]
    private bool _newIntegrationJobEnabled = true;

    [ObservableProperty]
    private string _newIntegrationJobCronExpression = string.Empty;

    public bool IsSqlConnectorSelected => NewConnectorType == ConnectorType.Sql;

    public bool IsCsvConnectorSelected => NewConnectorType == ConnectorType.Csv;

    public void SetCsvPath(string path)
    {
        CsvPath = path;
    }

    public void Dispose()
    {
        _refreshCts.Cancel();
        _refreshTimer.Dispose();
        _refreshCts.Dispose();
    }

    partial void OnNewConnectorTypeChanged(ConnectorType value)
    {
        OnPropertyChanged(nameof(IsSqlConnectorSelected));
        OnPropertyChanged(nameof(IsCsvConnectorSelected));
    }

    private async Task StartPollingAsync()
    {
        if (!IsAuthenticated)
        {
            return;
        }

        await RefreshAllAsync(_refreshCts.Token);

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(_refreshCts.Token))
            {
                await RefreshAllAsync(_refreshCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        if (!IsAuthenticated)
        {
            return;
        }

        if (_isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        IsLoading = true;

        try
        {
            var dashboardTask = _dashboardApiClient.GetDashboardAsync(cancellationToken);
            var connectorsTask = _connectorApiClient.GetAllAsync(cancellationToken);
            var jobsTask = _integrationJobsApiClient.GetAllAsync(cancellationToken);

            await Task.WhenAll(dashboardTask, connectorsTask, jobsTask);

            var dashboard = dashboardTask.Result;
            if (dashboard is not null)
            {
                TotalJobs = dashboard.TotalJobs;
                EnabledJobs = dashboard.EnabledJobs;
                FailedRunsLast24Hours = dashboard.FailedRunsLast24Hours;
                TotalConnectors = dashboard.TotalConnectors;
                LastUpdated = dashboard.GeneratedAtUtc.ToLocalTime().ToString("HH:mm:ss");
                ReplaceAll(DashboardJobs, dashboard.Jobs);
                ReplaceAll(DashboardRuns, dashboard.RecentRuns.OrderByDescending(run => run.TimestampUtc));
            }

            ReplaceAll(Connectors, connectorsTask.Result);
            ReplaceAll(IntegrationJobs, jobsTask.Result);

            if (SelectedSourceConnector is not null)
            {
                SelectedSourceConnector = Connectors.FirstOrDefault(item => item.Id == SelectedSourceConnector.Id);
            }

            if (SelectedDestinationConnector is not null)
            {
                SelectedDestinationConnector = Connectors.FirstOrDefault(item => item.Id == SelectedDestinationConnector.Id);
            }

            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Unable to refresh data from API.";
        }
        finally
        {
            IsLoading = false;
            _isRefreshing = false;
        }
    }

    private async Task CreateConnectorAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new CreateConnectorRequest(
                NewConnectorName,
                NewConnectorType,
                IsSqlConnectorSelected
                    ? new SqlConnectorSettingsInputDto(
                        SqlServer,
                        SqlPort,
                        SqlDatabase,
                        SqlUsername,
                        SqlPassword,
                        SqlTable,
                        SqlTrustServerCertificate)
                    : null,
                IsCsvConnectorSelected
                    ? new CsvConnectorSettingsDto(CsvPath, CsvDelimiter)
                    : null);

            var created = await _connectorApiClient.CreateAsync(request, cancellationToken);
            if (created is null)
            {
                ErrorMessage = "Failed to create connector. Check input and uniqueness rules.";
                return;
            }

            StatusMessage = $"Connector '{created.Name}' created.";
            ErrorMessage = null;
            NewConnectorName = string.Empty;
            SqlPassword = string.Empty;
            await RefreshAllAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Failed to create connector.";
        }
    }

    private async Task TestConnectorAsync(CancellationToken cancellationToken)
    {
        try
        {
            var request = new TestConnectorRequest(
                NewConnectorType,
                IsSqlConnectorSelected
                    ? new SqlConnectorSettingsInputDto(
                        SqlServer,
                        SqlPort,
                        SqlDatabase,
                        SqlUsername,
                        SqlPassword,
                        SqlTable,
                        SqlTrustServerCertificate)
                    : null,
                IsCsvConnectorSelected
                    ? new CsvConnectorSettingsDto(CsvPath, CsvDelimiter)
                    : null);

            var result = await _connectorApiClient.TestAsync(request, cancellationToken);
            if (result is null || !result.IsSuccess)
            {
                ErrorMessage = "Connector test failed.";
                return;
            }

            StatusMessage = result.Message;
            ErrorMessage = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Connector test failed.";
        }
    }

    private async Task CreateIntegrationJobAsync(CancellationToken cancellationToken)
    {
        if (SelectedSourceConnector is null || SelectedDestinationConnector is null)
        {
            ErrorMessage = "Select both source and destination connectors.";
            return;
        }

        try
        {
            var request = new CreateIntegrationJobRequest(
                NewIntegrationJobName,
                SelectedSourceConnector.Id,
                SelectedDestinationConnector.Id,
                NewIntegrationJobEnabled,
                string.IsNullOrWhiteSpace(NewIntegrationJobCronExpression)
                    ? null
                    : NewIntegrationJobCronExpression.Trim());

            var created = await _integrationJobsApiClient.CreateAsync(request, cancellationToken);
            if (created is null)
            {
                ErrorMessage = "Failed to create integration job. Check connector direction and unique name.";
                return;
            }

            StatusMessage = $"Integration job '{created.Name}' created.";
            ErrorMessage = null;
            NewIntegrationJobName = string.Empty;
            NewIntegrationJobCronExpression = string.Empty;
            await RefreshAllAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Failed to create integration job.";
        }
    }

    private async Task RunIntegrationJobAsync(IntegrationJobDto? job)
    {
        if (job is null)
        {
            return;
        }

        try
        {
            var response = await _integrationJobsApiClient.RunNowAsync(job.Id, _refreshCts.Token);
            if (response is null)
            {
                ErrorMessage = "Failed to queue integration job run.";
                return;
            }

            StatusMessage = $"Run queued for '{job.Name}' (Hangfire #{response.HangfireJobId}).";
            ErrorMessage = null;
            await RefreshAllAsync(_refreshCts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Failed to queue integration job run.";
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

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authApiClient.LoginAsync(
                new LoginRequest(LoginUsername, LoginPassword),
                cancellationToken);

            if (response is null)
            {
                ErrorMessage = "Invalid username or password.";
                return;
            }

            ApplyAuthenticatedUser(response);
            await StartPollingAsync();
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Login failed.";
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _authApiClient.RegisterAsync(
                new RegisterUserRequest(LoginUsername, LoginPassword),
                cancellationToken);

            if (response is null)
            {
                ErrorMessage = "Registration failed. Username may already exist.";
                return;
            }

            ApplyAuthenticatedUser(response);
            await StartPollingAsync();
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = "Registration failed.";
        }
    }

    private void ApplyAuthenticatedUser(AuthResponse response)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
        IsAuthenticated = true;
        CurrentUsername = response.Username;
        LoginPassword = string.Empty;
        ErrorMessage = null;
        StatusMessage = $"Signed in as {response.Username}.";
    }

    private void Logout()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
        IsAuthenticated = false;
        CurrentUsername = string.Empty;
        StatusMessage = "Signed out.";
        ReplaceAll(DashboardJobs, []);
        ReplaceAll(DashboardRuns, []);
        ReplaceAll(Connectors, []);
        ReplaceAll(IntegrationJobs, []);
    }

    partial void OnIsAuthenticatedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotAuthenticated));
    }
}
