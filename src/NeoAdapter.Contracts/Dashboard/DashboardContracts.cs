namespace NeoAdapter.Contracts.Dashboard;

public sealed record DashboardAnalyticsPoint(
    DateTimeOffset Date,
    int SuccessfulRuns,
    int FailedRuns);

public sealed record DashboardResponse(
    int TotalJobs,
    int EnabledJobs,
    int FailedRunsLast24Hours,
    int TotalConnectors,
    IReadOnlyList<DashboardJobSummaryItem> Jobs,
    IReadOnlyList<DashboardRunItem> RecentRuns,
    IReadOnlyList<DashboardAnalyticsPoint> Analytics,
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

public sealed record DashboardFilterRequest(
    Guid? OrganizationId = null,
    Guid? GroupId = null,
    Guid? UserId = null);
