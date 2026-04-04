using Hangfire;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;

namespace NeoAdapter.Application.IntegrationJobs;

public sealed class IntegrationJobScheduler(
    NeoAdapterDbContext dbContext,
    IRecurringJobManager recurringJobManager) : IIntegrationJobScheduler
{
    public void SyncJob(Guid jobId, bool isEnabled, string? cronExpression)
    {
        var recurringId = GetRecurringId(jobId);
        if (!isEnabled || string.IsNullOrWhiteSpace(cronExpression))
        {
            recurringJobManager.RemoveIfExists(recurringId);
            return;
        }

        recurringJobManager.AddOrUpdate<IIntegrationJobExecutor>(
            recurringId,
            executor => executor.ExecuteAsync(jobId),
            cronExpression);
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken)
    {
        var jobs = await dbContext.IntegrationJobs
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            SyncJob(job.Id, job.IsEnabled, job.CronExpression);
        }
    }

    public static string GetRecurringId(Guid jobId) => $"integration-job:{jobId}";
}