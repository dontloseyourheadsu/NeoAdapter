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
    string? LastRunMessage,
    IReadOnlyList<Guid>? GroupIds = null);

public sealed record CreateIntegrationJobStepRequest(
    int OrderIndex,
    ConnectorType SourceType,
    SqlConnectorSettingsInputDto? SourceSql,
    CsvConnectorSettingsDto? SourceCsv,
    ExcelConnectorSettingsDto? SourceExcel,
    PathConnectorSettingsDto? SourcePath,
    SftpConnectorSettingsInputDto? SourceSftp,
    ConnectorType DestinationType,
    SqlConnectorSettingsInputDto? DestinationSql,
    CsvConnectorSettingsDto? DestinationCsv,
    ExcelConnectorSettingsDto? DestinationExcel,
    PathConnectorSettingsDto? DestinationPath,
    SftpConnectorSettingsInputDto? DestinationSftp);


public sealed record CreateIntegrationJobRequest(
    string Name,
    IReadOnlyList<CreateIntegrationJobStepRequest> Steps,
    bool IsEnabled,
    string? CronExpression,
    Guid? OwnerUserId = null,
    Guid? OwnerGroupId = null,
    Guid? OwnerOrganizationId = null,
    IReadOnlyList<Guid>? GroupIds = null);

public sealed record RunIntegrationJobRequest(Guid IntegrationJobId);

public sealed record EnqueueIntegrationJobResponse(
    Guid IntegrationJobId,
    string HangfireJobId,
    DateTimeOffset EnqueuedAtUtc);

public sealed record IntegrationJobRunDto(
    Guid Id,
    Guid IntegrationJobId,
    string Status,
    string Message,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    int RecordsProcessed,
    string? HangfireJobId,
    string StartedBy);

public sealed record JobLogDto(
    Guid Id,
    Guid IntegrationJobId,
    Guid? IntegrationJobRunId,
    DateTimeOffset TimestampUtc,
    string LogLevel,
    string Message,
    string? Details);

public sealed record JobLogsResponse(
    IReadOnlyList<JobLogDto> Logs,
    DateTimeOffset? NextCursor);
