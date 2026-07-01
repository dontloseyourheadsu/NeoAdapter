namespace NeoAdapter.Contracts.Connectors;

public enum ConnectorType
{
    SqlServer,
    Postgres,
    Csv,
    Excel,
    Path,
    Sftp,
    SharePoint,
    OutlookCalendar
}

public sealed record ConnectorDto(
    Guid Id,
    string Name,
    ConnectorType Type,
    SqlConnectorSettingsDto? Sql,
    CsvConnectorSettingsDto? Csv,
    ExcelConnectorSettingsDto? Excel,
    PathConnectorSettingsDto? Path,
    SftpConnectorSettingsDto? Sftp,
    SharePointConnectorSettingsDto? SharePoint,
    OutlookCalendarConnectorSettingsDto? OutlookCalendar,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record SqlConnectorSettingsDto(
    string Host,
    int Port,
    string Database,
    string Username,
    bool TrustServerCertificate,
    string? ConfigJson);

public sealed record SqlConnectorSettingsInputDto(
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate,
    string? ConfigJson = null);

public sealed record CsvConnectorSettingsDto(
    string Path,
    string Delimiter);

public sealed record ExcelConnectorSettingsDto(
    string Path,
    string? SheetName);

public sealed record CreateConnectorRequest(
    string Name,
    ConnectorType Type,
    SqlConnectorSettingsInputDto? Sql = null,
    CsvConnectorSettingsDto? Csv = null,
    ExcelConnectorSettingsDto? Excel = null,
    PathConnectorSettingsDto? Path = null,
    SftpConnectorSettingsInputDto? Sftp = null,
    SharePointConnectorSettingsInputDto? SharePoint = null,
    OutlookCalendarConnectorSettingsInputDto? OutlookCalendar = null);


public sealed record TestConnectorRequest(
    ConnectorType Type,
    SqlConnectorSettingsInputDto? Sql = null,
    CsvConnectorSettingsDto? Csv = null,
    ExcelConnectorSettingsDto? Excel = null,
    PathConnectorSettingsDto? Path = null,
    SftpConnectorSettingsInputDto? Sftp = null,
    SharePointConnectorSettingsInputDto? SharePoint = null,
    OutlookCalendarConnectorSettingsInputDto? OutlookCalendar = null);

public sealed record SharePointConnectorSettingsDto(
    string SiteUrl,
    string ListName,
    string? ConfigJson);

public sealed record SharePointConnectorSettingsInputDto(
    string SiteUrl,
    string ListName,
    string? ConfigJson = null);

public sealed record SharePointFieldDto(
    string Title,
    string InternalName,
    string TypeAsString);

public sealed record PathConnectorSettingsDto(
    string Path);

public sealed record SftpConnectorSettingsDto(
    string Host,
    int Port,
    string Username,
    string RemotePath);

public sealed record SftpConnectorSettingsInputDto(
    string Host,
    int Port,
    string Username,
    string Password,
    string RemotePath);

public sealed record TestConnectorResponse(
    bool IsSuccess,
    string Message,
    DateTimeOffset TestedAtUtc);

public sealed record OutlookCalendarConnectorSettingsDto(
    string CalendarName,
    string? ConfigJson);

public sealed record OutlookCalendarConnectorSettingsInputDto(
    string CalendarName,
    string? ConfigJson = null);
