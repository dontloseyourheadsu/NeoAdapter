using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Dashboard;

namespace NeoAdapter.Application.Dashboard;

public sealed class DashboardService(NeoAdapterDbContext dbContext) : IDashboardService
{
    public async Task<DashboardResponse> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dayWindow = now.AddHours(-24);

        var jobs = await dbContext.IntegrationJobs
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(job => job.Id).ToArray();

        var runs = await dbContext.IntegrationJobRuns
            .AsNoTracking()
            .Where(run => jobIds.Contains(run.IntegrationJobId))
            .OrderByDescending(run => run.StartedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var latestRunsByJob = runs
            .GroupBy(run => run.IntegrationJobId)
            .ToDictionary(group => group.Key, group => group.First());

        var summaries = jobs
            .Select(job =>
            {
                latestRunsByJob.TryGetValue(job.Id, out var latestRun);
                return new DashboardJobSummaryItem(
                    job.Id,
                    job.Name,
                    $"{job.SourceConnectorId} -> {job.DestinationConnectorId}",
                    latestRun?.Status ?? (job.IsEnabled ? "READY" : "DISABLED"),
                    latestRun?.StartedAtUtc,
                    latestRun?.Message);
            })
            .ToArray();

        var recentRuns = runs
            .Take(25)
            .Select(run =>
            {
                var jobName = jobs.FirstOrDefault(job => job.Id == run.IntegrationJobId)?.Name ?? "Unknown job";
                var level = run.Status switch
                {
                    "FAILED" => "ERR",
                    "RUNNING" => "INFO",
                    "QUEUED" => "INFO",
                    _ => "INFO"
                };

                return new DashboardRunItem(
                    run.StartedAtUtc,
                    level,
                    run.Message,
                    jobName);
            })
            .ToArray();

        var failedLast24Hours = runs.Count(run => run.Status == "FAILED" && run.StartedAtUtc >= dayWindow);

        return new DashboardResponse(
            jobs.Count,
            jobs.Count(job => job.IsEnabled),
            failedLast24Hours,
            await dbContext.Connectors.CountAsync(cancellationToken),
            summaries,
            recentRuns,
            now);
    }
}
