using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeoAdapter.Contracts.IntegrationJobs;

namespace NeoAdapter.Application.IntegrationJobs;

public interface IIntegrationJobService
{
    Task<IReadOnlyList<IntegrationJobDto>> GetAllAsync(Guid userId, Guid organizationId, Guid? groupId, string role, CancellationToken cancellationToken);

    Task<IntegrationJobDto> CreateAsync(CreateIntegrationJobRequest request, CancellationToken cancellationToken);

    Task<EnqueueIntegrationJobResponse> EnqueueRunAsync(Guid integrationJobId, string startedBy, CancellationToken cancellationToken);

    Task<IReadOnlyList<IntegrationJobRunDto>> GetJobRunsAsync(Guid jobId, Guid userId, Guid organizationId, Guid? groupId, string role, CancellationToken cancellationToken);

    Task<JobLogsResponse> GetJobLogsAsync(Guid jobId, Guid? runId, DateTimeOffset? cursor, int limit, Guid userId, Guid organizationId, Guid? groupId, string role, CancellationToken cancellationToken);
}