using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Application.Dashboard;

public sealed class MockDashboardService : IDashboardService
{
    private static readonly string[] InfoTemplates =
    [
        "Scheduler heartbeat OK — 4 workers healthy",
        "Salesforce sync batch completed",
        "BigQuery aggregation stage 2 started",
        "HubSpot contact import queued",
        "Stripe reconcile retrying connection"
    ];

    public Task<DashboardResponse> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var activeJobs = 8 + Random.Shared.Next(0, 6);
        var queuedJobs = 14 + Random.Shared.Next(0, 12);
        var failedJobs = Random.Shared.Next(0, 4);
        var totalJobs = 220 + Random.Shared.Next(0, 40);

        var now = DateTimeOffset.UtcNow;

        IReadOnlyList<DashboardScheduleItem> schedule =
        [
            new("08:00", "ETL: Salesforce Sync", "45m", "RUNNING", "#8B44F7"),
            new("09:00", "Report Export", "12m", "RUNNING", "#3DFFC0"),
            new("10:00", "Warehouse Backup", "1h 10m", "WAITING", "#FFB547"),
            new("11:00", "BigQuery Aggregation", "28m", "RUNNING", "#5FC4FF"),
            new("13:00", "Stripe Reconcile", "FAILED", "FAILED", "#FF5F7E"),
            new("14:00", "HubSpot Contact Import", "QUEUED", "QUEUED", "#A66DFF")
        ];

        IReadOnlyList<DashboardJobItem> activeJobsList =
        [
            new("ETL: Salesforce Sync", "Started 08:02 · worker-01", "RUNNING", "#8B44F7"),
            new("BigQuery Pipeline", "Started 11:14 · worker-03", "RUNNING", "#5FC4FF"),
            new("HubSpot Contact Import", "Queued 14:00 · ETA ~5m", "QUEUED", "#A66DFF"),
            new("Nightly DB Backup", "Scheduled 00:00 · 10h", "WAITING", "#FFB547"),
            new("Stripe Reconcile", "Failed 13:04 · timeout", "FAILED", "#FF5F7E")
        ];

        IReadOnlyList<DashboardIntegrationItem> integrations =
        [
            new("Salesforce", 48, true, "#FFB547"),
            new("BigQuery", 31, true, "#5FC4FF"),
            new("HubSpot", 22, true, "#3DFFC0"),
            new("Stripe", 17, true, "#A66DFF"),
            new("Snowflake", 0, false, "#9B93B8")
        ];

        var logs = Enumerable.Range(0, 8)
            .Select(index => new DashboardLogItem(
                now.AddSeconds(-index * Random.Shared.Next(8, 25)),
                index % 5 == 0 ? "WARN" : index % 7 == 0 ? "ERR" : "INFO",
                InfoTemplates[Random.Shared.Next(InfoTemplates.Length)]))
            .ToArray();

        var response = new DashboardResponse(
            totalJobs,
            activeJobs,
            queuedJobs,
            failedJobs,
            schedule,
            activeJobsList,
            integrations,
            logs,
            now);

        return Task.FromResult(response);
    }
}