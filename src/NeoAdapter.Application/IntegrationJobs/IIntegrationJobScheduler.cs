namespace NeoAdapter.Application.IntegrationJobs;

public interface IIntegrationJobScheduler
{
    void SyncJob(Guid jobId, bool isEnabled, string? cronExpression);

    Task SyncAllAsync(CancellationToken cancellationToken);
}