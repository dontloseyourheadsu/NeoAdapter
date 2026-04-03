namespace NeoAdapter.Contracts.Dashboard;

public sealed record DashboardResponse(
    int TotalJobs,
    int EnabledJobs,
    int FailedRunsLast24Hours,
    int TotalConnectors,
    IReadOnlyList<DashboardJobSummaryItem> Jobs,
    IReadOnlyList<DashboardRunItem> RecentRuns,
    DateTimeOffset GeneratedAtUtc);

public sealed record DashboardJobSummaryItem(
    Guid Id,
    string Name,
    string Direction,
    string Status,
    DateTimeOffset? LastRunAtUtc,
    string? LastRunMessage);

public sealed record DashboardRunItem(
    DateTimeOffset TimestampUtc,
    string Level,
    string Message,
    string JobName);
