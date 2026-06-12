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
    IReadOnlyList<Guid>? GroupIds = null,
    bool IsPasswordProtected = false,
    bool IsUnlocked = true);

public sealed record CreateIntegrationJobStepRequest(
    int OrderIndex,
    ConnectorType SourceType,
    SqlConnectorSettingsInputDto? SourceSql = null,
    CsvConnectorSettingsDto? SourceCsv = null,
    ExcelConnectorSettingsDto? SourceExcel = null,
    PathConnectorSettingsDto? SourcePath = null,
    SftpConnectorSettingsInputDto? SourceSftp = null,
    ConnectorType DestinationType = ConnectorType.SqlServer,
    SqlConnectorSettingsInputDto? DestinationSql = null,
    CsvConnectorSettingsDto? DestinationCsv = null,
    ExcelConnectorSettingsDto? DestinationExcel = null,
    PathConnectorSettingsDto? DestinationPath = null,
    SftpConnectorSettingsInputDto? DestinationSftp = null);


public sealed record CreateIntegrationJobRequest(
    string Name,
    IReadOnlyList<CreateIntegrationJobStepRequest> Steps,
    bool IsEnabled,
    string? CronExpression,
    Guid? OwnerUserId = null,
    Guid? OwnerGroupId = null,
    Guid? OwnerOrganizationId = null,
    IReadOnlyList<Guid>? GroupIds = null,
    string? Password = null);

public sealed record UnlockJobRequest(string Password);

public sealed record UpdateJobPasswordRequest(string? Password);

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

public sealed record IntegrationJobGuestDto(
    Guid UserId,
    string Username,
    string? Email,
    bool CanRead,
    bool CanEdit,
    bool CanCreateConnectors);

public sealed record InviteGuestRequest(
    string Username,
    bool CanRead,
    bool CanEdit,
    bool CanCreateConnectors);

public sealed record UpdateGuestPermissionsRequest(
    bool CanRead,
    bool CanEdit,
    bool CanCreateConnectors);

public sealed record IntegrationJobOwnerDto(
    Guid UserId,
    string Username,
    string? Email,
    bool IsCreator);

public sealed record AddOwnerRequest(
    string Username);
