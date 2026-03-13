namespace NeoAdapter.Contracts.Dashboard;

public sealed record DashboardResponse(
    int TotalJobs,
    int ActiveJobs,
    int QueuedJobs,
    int FailedJobs,
    IReadOnlyList<DashboardScheduleItem> Schedule,
    IReadOnlyList<DashboardJobItem> ActiveJobsList,
    IReadOnlyList<DashboardIntegrationItem> Integrations,
    IReadOnlyList<DashboardLogItem> Logs,
    DateTimeOffset GeneratedAtUtc);

public sealed record DashboardScheduleItem(
    string TimeLabel,
    string JobName,
    string DurationLabel,
    string Status,
    string ColorHex);

public sealed record DashboardJobItem(
    string Name,
    string Meta,
    string Status,
    string ColorHex);

public sealed record DashboardIntegrationItem(
    string Name,
    int JobCount,
    bool Connected,
    string ColorHex);

public sealed record DashboardLogItem(
    DateTimeOffset TimestampUtc,
    string Level,
    string Message);
