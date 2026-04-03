using NeoAdapter.Contracts.IntegrationJobs;

namespace NeoAdapter.Application.IntegrationJobs;

public interface IIntegrationJobService
{
    Task<IReadOnlyList<IntegrationJobDto>> GetAllAsync(CancellationToken cancellationToken);

    Task<IntegrationJobDto> CreateAsync(CreateIntegrationJobRequest request, CancellationToken cancellationToken);

    Task<EnqueueIntegrationJobResponse> EnqueueRunAsync(Guid integrationJobId, CancellationToken cancellationToken);
}