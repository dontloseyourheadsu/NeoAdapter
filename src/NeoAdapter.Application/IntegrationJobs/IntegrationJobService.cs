using Hangfire;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Connectors;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.Connectors;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Domain;
using ConnectorTypeContract = NeoAdapter.Contracts.Connectors.ConnectorType;
using ConnectorTypeDomain = NeoAdapter.Domain.ConnectorType;

namespace NeoAdapter.Application.IntegrationJobs;

public sealed class IntegrationJobService(
    NeoAdapterDbContext dbContext,
    IBackgroundJobClient backgroundJobClient,
    IIntegrationJobScheduler integrationJobScheduler,
    IConnectorService connectorService) : IIntegrationJobService
{
    public async Task<IReadOnlyList<IntegrationJobDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var jobs = await dbContext.IntegrationJobs
            .AsNoTracking()
            .Include(job => job.Steps)
                .ThenInclude(step => step.SourceConnector)
            .Include(job => job.Steps)
                .ThenInclude(step => step.DestinationConnector)
            .OrderBy(job => job.Name)
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(job => job.Id).ToList();
        
        // Fetch runs separately to avoid MARS issues on Postgres
        var latestRuns = await dbContext.IntegrationJobRuns
            .AsNoTracking()
            .Where(run => jobIds.Contains(run.IntegrationJobId))
            .OrderByDescending(run => run.StartedAtUtc)
            .ToListAsync(cancellationToken);

        var latestRunsMap = latestRuns
            .GroupBy(run => run.IntegrationJobId)
            .ToDictionary(g => g.Key, g => g.First());

        return jobs.Select(job => MapToDto(job, latestRunsMap.GetValueOrDefault(job.Id))).ToArray();
    }

    public async Task<IntegrationJobDto> CreateAsync(CreateIntegrationJobRequest request, CancellationToken cancellationToken)
    {
        var trimmedName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new InvalidOperationException("Integration job name is required.");
        }

        var existingName = await dbContext.IntegrationJobs
            .AnyAsync(job => job.Name.ToLower() == trimmedName.ToLower(), cancellationToken);
        if (existingName)
        {
            throw new InvalidOperationException("An integration job with this name already exists.");
        }

        if (request.Steps == null || request.Steps.Count == 0)
        {
            throw new InvalidOperationException("Integration job must have at least one step.");
        }

        // Ownership validation
        if (request.OwnerUserId == null && request.OwnerGroupId == null && request.OwnerOrganizationId == null)
        {
            throw new InvalidOperationException("Integration job must have an owner (User, Group, or Organization).");
        }

        var now = DateTimeOffset.UtcNow;
        var cronExpression = string.IsNullOrWhiteSpace(request.CronExpression)
            ? null
            : request.CronExpression.Trim();

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            OwnerUserId = request.OwnerUserId,
            OwnerGroupId = request.OwnerGroupId,
            OwnerOrganizationId = request.OwnerOrganizationId,
            IsEnabled = request.IsEnabled,
            CronExpression = cronExpression,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var stepRequest in request.Steps.OrderBy(s => s.OrderIndex))
        {
            var sourceConnectorDto = await connectorService.CreateAsync(new CreateConnectorRequest(
                $"{trimmedName} - Step {stepRequest.OrderIndex} Source",
                stepRequest.SourceType,
                stepRequest.SourceSql,
                stepRequest.SourceCsv), cancellationToken);

            var destinationConnectorDto = await connectorService.CreateAsync(new CreateConnectorRequest(
                $"{trimmedName} - Step {stepRequest.OrderIndex} Destination",
                stepRequest.DestinationType,
                stepRequest.DestinationSql,
                stepRequest.DestinationCsv), cancellationToken);

            job.Steps.Add(new IntegrationJobStep
            {
                Id = Guid.NewGuid(),
                IntegrationJobId = job.Id,
                OrderIndex = stepRequest.OrderIndex,
                SourceConnectorId = sourceConnectorDto.Id,
                DestinationConnectorId = destinationConnectorDto.Id
            });
        }

        dbContext.IntegrationJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        integrationJobScheduler.SyncJob(job.Id, job.IsEnabled, job.CronExpression);

        // Reload to include relations for DTO mapping
        var savedJob = await dbContext.IntegrationJobs
            .Include(j => j.Steps)
                .ThenInclude(s => s.SourceConnector)
            .Include(j => j.Steps)
                .ThenInclude(s => s.DestinationConnector)
            .FirstAsync(j => j.Id == job.Id, cancellationToken);

        return MapToDto(savedJob, null);
    }

    public async Task<EnqueueIntegrationJobResponse> EnqueueRunAsync(Guid integrationJobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.IntegrationJobs
            .FirstOrDefaultAsync(item => item.Id == integrationJobId, cancellationToken)
            ?? throw new InvalidOperationException("Integration job not found.");

        if (!job.IsEnabled)
        {
            throw new InvalidOperationException("Integration job is disabled.");
        }

        var run = new IntegrationJobRun
        {
            Id = Guid.NewGuid(),
            IntegrationJobId = integrationJobId,
            Status = "QUEUED",
            Message = "Queued for execution.",
            StartedAtUtc = DateTimeOffset.UtcNow,
            RecordsProcessed = 0
        };

        dbContext.IntegrationJobRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        var hangfireJobId = backgroundJobClient.Enqueue<IIntegrationJobExecutor>(executor => executor.ExecuteAsync(integrationJobId));

        run.HangfireJobId = hangfireJobId;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new EnqueueIntegrationJobResponse(integrationJobId, hangfireJobId, DateTimeOffset.UtcNow);
    }

    private static IntegrationJobDto MapToDto(IntegrationJob job, IntegrationJobRun? latestRun)
    {
        var steps = job.Steps
            .OrderBy(s => s.OrderIndex)
            .Select(s => new IntegrationJobStepDto(
                s.Id,
                s.OrderIndex,
                s.SourceConnectorId,
                s.DestinationConnectorId,
                s.SourceConnector?.Name ?? "Unknown source",
                s.DestinationConnector?.Name ?? "Unknown destination"))
            .ToList();

        return new IntegrationJobDto(
            job.Id,
            job.Name,
            steps,
            job.IsEnabled,
            job.CronExpression,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            latestRun?.StartedAtUtc,
            latestRun?.Status,
            latestRun?.Message);
    }
}
