namespace NeoAdapter.Contracts.IntegrationJobs;

public sealed record IntegrationJobDto(
    Guid Id,
    string Name,
    Guid SourceConnectorId,
    Guid DestinationConnectorId,
    string SourceConnectorName,
    string DestinationConnectorName,
    string DirectionLabel,
    bool IsEnabled,
    string? CronExpression,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastRunAtUtc,
    string? LastRunStatus,
    string? LastRunMessage);

public sealed record CreateIntegrationJobRequest(
    string Name,
    Guid SourceConnectorId,
    Guid DestinationConnectorId,
    bool IsEnabled,
    string? CronExpression);

public sealed record RunIntegrationJobRequest(Guid IntegrationJobId);

public sealed record EnqueueIntegrationJobResponse(
    Guid IntegrationJobId,
    string HangfireJobId,
    DateTimeOffset EnqueuedAtUtc);