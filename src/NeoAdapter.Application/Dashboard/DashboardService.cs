using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Dashboard;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.Dashboard;

public sealed class DashboardService(NeoAdapterDbContext dbContext) : IDashboardService
{
    public async Task<DashboardResponse> GetDashboardAsync(DashboardFilterRequest filter, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dayWindow = now.AddHours(-24);
        var analyticsWindow = now.AddDays(-7).Date;

        IQueryable<IntegrationJob> jobsQuery = dbContext.IntegrationJobs
            .AsNoTracking()
            .Include(j => j.Steps)
                .ThenInclude(s => s.SourceConnector)
            .Include(j => j.Steps)
                .ThenInclude(s => s.DestinationConnector);

        if (filter.OrganizationId.HasValue)
            jobsQuery = jobsQuery.Where(j => j.OwnerOrganizationId == filter.OrganizationId);
        if (filter.GroupId.HasValue)
            jobsQuery = jobsQuery.Where(j => j.OwnerGroupId == filter.GroupId || j.Groups.Any(g => g.Id == filter.GroupId.Value));
        if (filter.UserId.HasValue)
            jobsQuery = jobsQuery.Where(j => j.OwnerUserId == filter.UserId);

        var jobs = await jobsQuery
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(job => job.Id).ToList();

        // Avoid MARS issues on Postgres by fetching runs separately
        var runs = await dbContext.IntegrationJobRuns
            .AsNoTracking()
            .Where(run => jobIds.Contains(run.IntegrationJobId))
            .OrderByDescending(run => run.StartedAtUtc)
            .ToListAsync(cancellationToken);

        var latestRunsByJob = runs
            .GroupBy(run => run.IntegrationJobId)
            .ToDictionary(group => group.Key, group => group.First());

        var summaries = jobs
            .Select(job =>
            {
                latestRunsByJob.TryGetValue(job.Id, out var latestRun);
                var direction = job.Steps.Count > 0 
                    ? string.Join(" | ", job.Steps.OrderBy(s => s.OrderIndex).Select(s => $"{s.SourceConnector?.Name ?? "?"} -> {s.DestinationConnector?.Name ?? "?"}"))
                    : "No Steps";

                return new DashboardJobSummaryItem(
                    job.Id,
                    job.Name,
                    direction,
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

        // Analytics: Successful vs Failed runs per day for the last 7 days
        var analytics = runs
            .Where(r => r.StartedAtUtc >= analyticsWindow)
            .GroupBy(r => r.StartedAtUtc.Date)
            .Select(g => new DashboardAnalyticsPoint(
                new DateTimeOffset(g.Key, TimeSpan.Zero),
                g.Count(r => r.Status == "SUCCEEDED"),
                g.Count(r => r.Status == "FAILED")))
            .OrderBy(p => p.Date)
            .ToList();

        // Fill in missing days with zeros
        for (int i = 0; i < 7; i++)
        {
            var date = now.AddDays(-i).Date;
            if (!analytics.Any(p => p.Date.Date == date))
            {
                analytics.Add(new DashboardAnalyticsPoint(new DateTimeOffset(date, TimeSpan.Zero), 0, 0));
            }
        }
        analytics = analytics.OrderBy(p => p.Date).ToList();

        return new DashboardResponse(
            jobs.Count,
            jobs.Count(job => job.IsEnabled),
            failedLast24Hours,
            await dbContext.Connectors.CountAsync(cancellationToken),
            summaries,
            recentRuns,
            analytics,
            now);
    }
}
