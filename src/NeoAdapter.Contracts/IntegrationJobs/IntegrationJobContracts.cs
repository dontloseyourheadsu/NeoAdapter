using NeoAdapter.Contracts.Connectors;

namespace NeoAdapter.Contracts.IntegrationJobs;

public sealed record IntegrationJobStepDto(
    Guid Id,
    int OrderIndex,
    Guid SourceConnectorId,
    Guid DestinationConnectorId,
    string SourceConnectorName,
    string DestinationConnectorName);

public sealed record IntegrationJobDto(
    Guid Id,
    string Name,
    IReadOnlyList<IntegrationJobStepDto> Steps,
    bool IsEnabled,
    string? CronExpression,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? LastRunAtUtc,
    string? LastRunStatus,
    string? LastRunMessage);

public sealed record CreateIntegrationJobStepRequest(
    int OrderIndex,
    ConnectorType SourceType,
    SqlConnectorSettingsInputDto? SourceSql,
    CsvConnectorSettingsDto? SourceCsv,
    ConnectorType DestinationType,
    SqlConnectorSettingsInputDto? DestinationSql,
    CsvConnectorSettingsDto? DestinationCsv);

public sealed record CreateIntegrationJobRequest(
    string Name,
    IReadOnlyList<CreateIntegrationJobStepRequest> Steps,
    bool IsEnabled,
    string? CronExpression,
    Guid? OwnerUserId = null,
    Guid? OwnerGroupId = null,
    Guid? OwnerOrganizationId = null);

public sealed record RunIntegrationJobRequest(Guid IntegrationJobId);

public sealed record EnqueueIntegrationJobResponse(
    Guid IntegrationJobId,
    string HangfireJobId,
    DateTimeOffset EnqueuedAtUtc);
