using Hangfire;
using Microsoft.EntityFrameworkCore;
using NeoAdapter.Application.Database.Contexts;
using NeoAdapter.Contracts.IntegrationJobs;
using NeoAdapter.Domain;

namespace NeoAdapter.Application.IntegrationJobs;

public sealed class IntegrationJobService(
    NeoAdapterDbContext dbContext,
    IBackgroundJobClient backgroundJobClient,
    IIntegrationJobScheduler integrationJobScheduler) : IIntegrationJobService
{
    public async Task<IReadOnlyList<IntegrationJobDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        var jobs = await dbContext.IntegrationJobs
            .AsNoTracking()
            .Include(job => job.SourceConnector)
            .Include(job => job.DestinationConnector)
            .OrderBy(job => job.Name)
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(job => job.Id).ToArray();
        var latestRuns = await dbContext.IntegrationJobRuns
            .Where(run => jobIds.Contains(run.IntegrationJobId))
            .GroupBy(run => run.IntegrationJobId)
            .Select(group => group
                .OrderByDescending(run => run.StartedAtUtc)
                .First())
            .ToDictionaryAsync(run => run.IntegrationJobId, cancellationToken);

        return jobs.Select(job => MapToDto(job, latestRuns.GetValueOrDefault(job.Id))).ToArray();
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

        var source = await dbContext.Connectors
            .FirstOrDefaultAsync(connector => connector.Id == request.SourceConnectorId, cancellationToken)
            ?? throw new InvalidOperationException("Source connector was not found.");

        var destination = await dbContext.Connectors
            .FirstOrDefaultAsync(connector => connector.Id == request.DestinationConnectorId, cancellationToken)
            ?? throw new InvalidOperationException("Destination connector was not found.");

        if (!IsSupportedDirection(source.Type, destination.Type))
        {
            throw new InvalidOperationException("Only SQL->CSV and CSV->SQL integrations are currently supported.");
        }

        var now = DateTimeOffset.UtcNow;
        var cronExpression = string.IsNullOrWhiteSpace(request.CronExpression)
            ? null
            : request.CronExpression.Trim();

        var job = new IntegrationJob
        {
            Id = Guid.NewGuid(),
            Name = trimmedName,
            SourceConnectorId = source.Id,
            DestinationConnectorId = destination.Id,
            IsEnabled = request.IsEnabled,
            CronExpression = cronExpression,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            SourceConnector = source,
            DestinationConnector = destination
        };

        dbContext.IntegrationJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        integrationJobScheduler.SyncJob(job.Id, job.IsEnabled, job.CronExpression);
        return MapToDto(job, null);
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

    private static bool IsSupportedDirection(ConnectorType source, ConnectorType destination)
    {
        return (source == ConnectorType.Sql && destination == ConnectorType.Csv)
               || (source == ConnectorType.Csv && destination == ConnectorType.Sql);
    }

    private static IntegrationJobDto MapToDto(IntegrationJob job, IntegrationJobRun? latestRun)
    {
        var sourceName = job.SourceConnector?.Name ?? "Unknown source";
        var destinationName = job.DestinationConnector?.Name ?? "Unknown destination";

        return new IntegrationJobDto(
            job.Id,
            job.Name,
            job.SourceConnectorId,
            job.DestinationConnectorId,
            sourceName,
            destinationName,
            $"{sourceName} -> {destinationName}",
            job.IsEnabled,
            job.CronExpression,
            job.CreatedAtUtc,
            job.UpdatedAtUtc,
            latestRun?.StartedAtUtc,
            latestRun?.Status,
            latestRun?.Message);
    }
}