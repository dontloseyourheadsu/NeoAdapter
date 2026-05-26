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
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.Dashboard;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Frontend.Services;

namespace NeoAdapter.Frontend.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    // ... rest of the file ...
    [ObservableProperty]
    private Guid? _selectedFilterOrganizationId;

    [ObservableProperty]
    private Guid? _selectedFilterGroupId;

    [ObservableProperty]
    private Guid? _selectedFilterUserId;

    public ISeries[] DashboardSeries { get; } = 
    [
        new LineSeries<int> { Values = new List<int> { 0, 0, 0, 0, 0, 0, 0 }, Name = "Successful", Stroke = new SolidColorPaint(SKColors.SpringGreen) { StrokeThickness = 2 } },
        new LineSeries<int> { Values = new List<int> { 0, 0, 0, 0, 0, 0, 0 }, Name = "Failed", Stroke = new SolidColorPaint(SKColors.Crimson) { StrokeThickness = 2 } }
    ];

    public Axis[] DashboardXAxes { get; } = [new Axis { LabelsRotation = 15 }];
    private readonly HttpClient _httpClient;
    private readonly AuthApiClient _authApiClient;
    private readonly DashboardApiClient _dashboardApiClient;
    private readonly ConnectorApiClient _connectorApiClient;
    private readonly IntegrationJobsApiClient _integrationJobsApiClient;
    private readonly TokenStorage _tokenStorage;
    private readonly PeriodicTimer _refreshTimer = new(TimeSpan.FromSeconds(8));
    private readonly CancellationTokenSource _refreshCts = new();
    private bool _isRefreshing;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;
    private string? _currentRefreshToken;

    public MainViewModel()
        : this(
            new HttpClient { BaseAddress = new Uri("http://localhost:5193/") },
            new AuthApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5193/") }),
            new DashboardApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5193/") }),
            new ConnectorApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5193/") }),
            new IntegrationJobsApiClient(new HttpClient { BaseAddress = new Uri("http://localhost:5193/") }),
            new TokenStorage())
    {
    }

    public MainViewModel(
        HttpClient httpClient,
        AuthApiClient authApiClient,
        DashboardApiClient dashboardApiClient,
        ConnectorApiClient connectorApiClient,
        IntegrationJobsApiClient integrationJobsApiClient,
        TokenStorage tokenStorage)
    {
        _httpClient = httpClient;
        _authApiClient = authApiClient;
        _dashboardApiClient = dashboardApiClient;
        _connectorApiClient = connectorApiClient;
        _integrationJobsApiClient = integrationJobsApiClient;
        _tokenStorage = tokenStorage;

        LoginCommand = new AsyncRelayCommand(() => LoginAsync(_refreshCts.Token));
        RegisterCommand = new AsyncRelayCommand(() => RegisterAsync(_refreshCts.Token));
        LogoutCommand = new RelayCommand(Logout);
        RefreshCommand = new AsyncRelayCommand(() => RefreshAllAsync(_refreshCts.Token));
        CreateConnectorCommand = new AsyncRelayCommand(() => CreateConnectorAsync(_refreshCts.Token));
        TestConnectorCommand = new AsyncRelayCommand(() => TestConnectorAsync(_refreshCts.Token));
        CreateIntegrationJobCommand = new AsyncRelayCommand(() => CreateIntegrationJobAsync(_refreshCts.Token));
        RunIntegrationJobCommand = new AsyncRelayCommand<IntegrationJobDto>(RunIntegrationJobAsync);
        LoadMoreLogsCommand = new AsyncRelayCommand(() => LoadLogEntriesAsync(SelectedLogRun, reset: false));
        ViewJobLogsCommand = new RelayCommand<IntegrationJobDto>(ViewJobLogs);
        ViewJobLogsFromSummaryCommand = new RelayCommand<DashboardJobSummaryItem>(ViewJobLogsFromSummary);
    }

    public ObservableCollection<DashboardJobSummaryItem> DashboardJobs { get; } = [];

    public ObservableCollection<DashboardRunItem> DashboardRuns { get; } = [];

    public ObservableCollection<ConnectorDto> Connectors { get; } = [];

    public ObservableCollection<IntegrationJobDto> IntegrationJobs { get; } = [];

    public IReadOnlyList<ConnectorType> ConnectorTypes { get; } = [ConnectorType.SqlServer, ConnectorType.Postgres, ConnectorType.Csv, ConnectorType.Excel];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand LoginCommand { get; }

    public IAsyncRelayCommand RegisterCommand { get; }

    public IRelayCommand LogoutCommand { get; }

    public IAsyncRelayCommand CreateConnectorCommand { get; }

    public IAsyncRelayCommand TestConnectorCommand { get; }

    public IAsyncRelayCommand CreateIntegrationJobCommand { get; }

    public IAsyncRelayCommand<IntegrationJobDto> RunIntegrationJobCommand { get; }

    public IAsyncRelayCommand LoadMoreLogsCommand { get; }

    public IRelayCommand<IntegrationJobDto> ViewJobLogsCommand { get; }

    public IRelayCommand<DashboardJobSummaryItem> ViewJobLogsFromSummaryCommand { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private IntegrationJobDto? _selectedLogJob;

    [ObservableProperty]
    private IntegrationJobRunDto? _selectedLogRun;

    [ObservableProperty]
    private bool _logHasMore;

    [ObservableProperty]
    private bool _logIsLoading;

    public ObservableCollection<IntegrationJobRunDto> LogRuns { get; } = [];

    public ObservableCollection<JobLogDto> LogEntries { get; } = [];

    private DateTimeOffset? _logNextCursor;

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
    private bool _rememberMe = true;

    [ObservableProperty]
    private string _newConnectorName = string.Empty;

    [ObservableProperty]
    private ConnectorType _newConnectorType = ConnectorType.SqlServer;

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
    private string _excelPath = string.Empty;

    [ObservableProperty]
    private string _excelSheetName = string.Empty;

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

    public bool IsSqlConnectorSelected => NewConnectorType == ConnectorType.SqlServer || NewConnectorType == ConnectorType.Postgres;

    public bool IsCsvConnectorSelected => NewConnectorType == ConnectorType.Csv;

    public bool IsExcelConnectorSelected => NewConnectorType == ConnectorType.Excel;

    public void SetCsvPath(string path)
    {
        CsvPath = path;
    }

    public void SetExcelPath(string path)
    {
        ExcelPath = path;
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
        OnPropertyChanged(nameof(IsExcelConnectorSelected));
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
        if (!await EnsureAuthenticatedAsync(cancellationToken))
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
            var filter = new DashboardFilterRequest(SelectedFilterOrganizationId, SelectedFilterGroupId, SelectedFilterUserId);
            var dashboardTask = _dashboardApiClient.GetDashboardAsync(filter, cancellationToken);
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

                // Update Chart
                if (DashboardSeries[0] is LineSeries<int> successSeries)
                    successSeries.Values = dashboard.Analytics.Select(a => a.SuccessfulRuns).ToList();
                
                if (DashboardSeries[1] is LineSeries<int> failSeries)
                    failSeries.Values = dashboard.Analytics.Select(a => a.FailedRuns).ToList();

                DashboardXAxes[0].Labels = dashboard.Analytics.Select(a => a.Date.ToLocalTime().ToString("MMM dd")).ToList();
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
        if (!await EnsureAuthenticatedAsync(cancellationToken)) return;
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
                        SqlTrustServerCertificate,
                        null) // ConfigJson
                    : null,
                IsCsvConnectorSelected
                    ? new CsvConnectorSettingsDto(CsvPath, CsvDelimiter)
                    : null,
                IsExcelConnectorSelected
                    ? new ExcelConnectorSettingsDto(ExcelPath, string.IsNullOrWhiteSpace(ExcelSheetName) ? null : ExcelSheetName)
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
        if (!await EnsureAuthenticatedAsync(cancellationToken)) return;
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
                        SqlTrustServerCertificate,
                        null) // ConfigJson
                    : null,
                IsCsvConnectorSelected
                    ? new CsvConnectorSettingsDto(CsvPath, CsvDelimiter)
                    : null,
                IsExcelConnectorSelected
                    ? new ExcelConnectorSettingsDto(ExcelPath, string.IsNullOrWhiteSpace(ExcelSheetName) ? null : ExcelSheetName)
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
        if (!await EnsureAuthenticatedAsync(cancellationToken)) return;
        
        // This is a temporary shim to fix the build. 
        // Real multi-step UI will replace this in the next phase.
        if (SelectedSourceConnector is null || SelectedDestinationConnector is null)
        {
            ErrorMessage = "Select both source and destination connectors.";
            return;
        }

        try
        {
            var steps = new List<CreateIntegrationJobStepRequest>
            {
                new CreateIntegrationJobStepRequest(
                    0,
                    SelectedSourceConnector.Type,
                    SelectedSourceConnector.Sql != null 
                        ? new SqlConnectorSettingsInputDto(
                            SelectedSourceConnector.Sql.Host,
                            SelectedSourceConnector.Sql.Port,
                            SelectedSourceConnector.Sql.Database,
                            SelectedSourceConnector.Sql.Username,
                            string.Empty, // Password not available in DTO for security
                            SelectedSourceConnector.Sql.TrustServerCertificate,
                            SelectedSourceConnector.Sql.ConfigJson)
                        : null,
                    SelectedSourceConnector.Csv,
                    SelectedDestinationConnector.Type,
                    SelectedDestinationConnector.Sql != null
                        ? new SqlConnectorSettingsInputDto(
                            SelectedDestinationConnector.Sql.Host,
                            SelectedDestinationConnector.Sql.Port,
                            SelectedDestinationConnector.Sql.Database,
                            SelectedDestinationConnector.Sql.Username,
                            string.Empty, // Password not available in DTO for security
                            SelectedDestinationConnector.Sql.TrustServerCertificate,
                            SelectedDestinationConnector.Sql.ConfigJson)
                        : null,
                    SelectedDestinationConnector.Csv)
            };

            var request = new CreateIntegrationJobRequest(
                NewIntegrationJobName,
                steps,
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

        if (!await EnsureAuthenticatedAsync(_refreshCts.Token)) return;

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
                new LoginRequest(LoginUsername, LoginPassword, RememberMe),
                cancellationToken);

            if (response is null)
            {
                ErrorMessage = "Invalid username or password.";
                return;
            }

            await ApplyAuthenticatedUserAsync(response);
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

            await ApplyAuthenticatedUserAsync(response);
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

    private async Task ApplyAuthenticatedUserAsync(AuthResponse response)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
        _accessTokenExpiresAt = response.ExpiresAtUtc;
        _currentRefreshToken = response.RefreshToken;
        
        IsAuthenticated = true;
        CurrentUsername = response.Username;
        LoginPassword = string.Empty;
        ErrorMessage = null;
        StatusMessage = $"Signed in as {response.Username}.";

        if (!string.IsNullOrEmpty(response.RefreshToken))
        {
            await _tokenStorage.SaveAsync(new StoredTokens(response.AccessToken, response.RefreshToken, response.ExpiresAtUtc));
        }
    }

    private void Logout()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
        _accessTokenExpiresAt = DateTimeOffset.MinValue;
        _currentRefreshToken = null;
        _tokenStorage.Clear();

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

    public async Task TryAutoLoginAsync()
    {
        if (IsAuthenticated || _isRefreshing) return;
        
        var tokens = await _tokenStorage.LoadAsync();
        if (tokens is null) return;

        _currentRefreshToken = tokens.RefreshToken;
        
        // Even if access token is not expired, we might want to refresh it to be safe 
        // or just apply it. Let's try to refresh immediately to ensure the session is still valid.
        await EnsureAuthenticatedAsync(_refreshCts.Token);

        if (IsAuthenticated)
        {
            await StartPollingAsync();
        }
    }

    private async Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_currentRefreshToken)) return IsAuthenticated;

        // If token expires in less than 2 minutes, refresh it
        if (_accessTokenExpiresAt < DateTimeOffset.UtcNow.AddMinutes(2))
        {
            try
            {
                var response = await _authApiClient.RefreshAsync(new RefreshTokenRequest(_currentRefreshToken), cancellationToken);
                if (response is not null)
                {
                    await ApplyAuthenticatedUserAsync(response);
                    return true;
                }
                else
                {
                    Logout();
                    return false;
                }
            }
            catch
            {
                // If refresh fails (e.g. network error), we don't necessarily want to logout
                // unless it's a 401/403 which RefreshAsync already handles by returning null.
                return IsAuthenticated;
            }
        }

        return IsAuthenticated;
    }

    private void ViewJobLogs(IntegrationJobDto? job)
    {
        if (job is null) return;
        SelectedLogJob = IntegrationJobs.FirstOrDefault(j => j.Id == job.Id) ?? job;
        SelectedTabIndex = 2;
    }

    private void ViewJobLogsFromSummary(DashboardJobSummaryItem? summary)
    {
        if (summary is null) return;
        var job = IntegrationJobs.FirstOrDefault(j => j.Id == summary.Id);
        if (job is not null)
        {
            ViewJobLogs(job);
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 2 && SelectedLogJob is null && IntegrationJobs.Count > 0)
        {
            SelectedLogJob = IntegrationJobs[0];
        }
    }

    partial void OnSelectedLogJobChanged(IntegrationJobDto? value)
    {
        _ = LoadLogRunsAsync(value);
    }

    partial void OnSelectedLogRunChanged(IntegrationJobRunDto? value)
    {
        _ = LoadLogEntriesAsync(value, reset: true);
    }

    private async Task LoadLogRunsAsync(IntegrationJobDto? job)
    {
        LogRuns.Clear();
        LogEntries.Clear();
        SelectedLogRun = null;
        _logNextCursor = null;
        LogHasMore = false;

        if (job is null) return;

        LogIsLoading = true;
        try
        {
            var runs = await _integrationJobsApiClient.GetRunsAsync(job.Id, _refreshCts.Token);
            ReplaceAll(LogRuns, runs);
            if (LogRuns.Count > 0)
            {
                SelectedLogRun = LogRuns[0];
            }
            else
            {
                _ = LoadLogEntriesAsync(null, reset: true);
            }
        }
        catch
        {
            ErrorMessage = "Failed to load execution runs.";
        }
        finally
        {
            LogIsLoading = false;
        }
    }

    private async Task LoadLogEntriesAsync(IntegrationJobRunDto? run, bool reset)
    {
        if (SelectedLogJob is null) return;

        LogIsLoading = true;
        try
        {
            if (reset)
            {
                LogEntries.Clear();
                _logNextCursor = null;
                LogHasMore = false;
            }

            var response = await _integrationJobsApiClient.GetLogsAsync(
                SelectedLogJob.Id,
                run?.Id,
                _logNextCursor,
                50,
                _refreshCts.Token);

            if (response is not null)
            {
                foreach (var log in response.Logs)
                {
                    LogEntries.Add(log);
                }
                _logNextCursor = response.NextCursor;
                LogHasMore = response.NextCursor is not null;
            }
        }
        catch
        {
            ErrorMessage = "Failed to load log entries.";
        }
        finally
        {
            LogIsLoading = false;
        }
    }
}
