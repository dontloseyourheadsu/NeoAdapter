namespace NeoAdapter.Application.IntegrationJobs;

public interface IIntegrationJobExecutor
{
    Task ExecuteAsync(Guid integrationJobId);
}